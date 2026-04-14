using System.Collections.Generic;
using System.Threading.Tasks;
using ClipHive;
using ClipHive.ViewModels;
using Moq;
using Xunit;

namespace ClipHive.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SidebarViewModel"/>.
/// All service dependencies are mocked with Moq — no real storage or paste calls.
/// </summary>
public sealed class SidebarViewModelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClipboardItem MakeItem(long id, string content, bool pinned = false) =>
        new(id, content, "iv==", "tag==", DateTime.UtcNow, null, pinned);

    private static (SidebarViewModel vm, Mock<IStorageService> storageMock, Mock<IPasteService> pasteMock)
        CreateSut(IReadOnlyList<ClipboardItem>? items = null)
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.GetAllAsync())
               .ReturnsAsync(items ?? Array.Empty<ClipboardItem>());

        var paste = new Mock<IPasteService>();
        paste.Setup(p => p.PasteAsync(It.IsAny<string>()))
             .Returns(Task.CompletedTask);

        var vm = new SidebarViewModel(storage.Object, paste.Object);
        return (vm, storage, paste);
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullStorage_ThrowsArgumentNullException()
    {
        var paste = new Mock<IPasteService>();
        Assert.Throws<ArgumentNullException>(() =>
            new SidebarViewModel(null!, paste.Object));
    }

    [Fact]
    public void Constructor_NullPaste_ThrowsArgumentNullException()
    {
        var storage = new Mock<IStorageService>();
        Assert.Throws<ArgumentNullException>(() =>
            new SidebarViewModel(storage.Object, null!));
    }

    [Fact]
    public void Constructor_InitialisesEmptyItems()
    {
        var (vm, _, _) = CreateSut();
        Assert.Empty(vm.Items);
    }

    [Fact]
    public void Constructor_SearchTextIsEmpty()
    {
        var (vm, _, _) = CreateSut();
        Assert.Equal(string.Empty, vm.SearchText);
    }

    // ── SearchText filtering ──────────────────────────────────────────────────

    [Fact]
    public void SearchText_EmptyString_ReturnsAllItems()
    {
        var (vm, _, _) = CreateSut();
        vm.Items.Add(new ClipboardItemViewModel(MakeItem(1, "hello world"), "hello world"));
        vm.Items.Add(new ClipboardItemViewModel(MakeItem(2, "foo bar"), "foo bar"));

        vm.SearchText = string.Empty;

        Assert.Equal(2, vm.FilteredItems.Count);
    }

    [Fact]
    public void SearchText_CaseInsensitiveMatch_ReturnsMatchingItems()
    {
        var (vm, _, _) = CreateSut();
        vm.Items.Add(new ClipboardItemViewModel(MakeItem(1, "Hello World"), "Hello World"));
        vm.Items.Add(new ClipboardItemViewModel(MakeItem(2, "foo bar"), "foo bar"));

        vm.SearchText = "hello";

        Assert.Single(vm.FilteredItems);
        Assert.Equal("Hello World", vm.FilteredItems[0].DecryptedContent);
    }

    [Fact]
    public void SearchText_NoMatch_ReturnsEmpty()
    {
        var (vm, _, _) = CreateSut();
        vm.Items.Add(new ClipboardItemViewModel(MakeItem(1, "hello world"), "hello world"));

        vm.SearchText = "zzz_no_match_zzz";

        Assert.Empty(vm.FilteredItems);
    }

    [Fact]
    public void SearchText_PartialMatch_ReturnsMatchingItems()
    {
        var (vm, _, _) = CreateSut();
        vm.Items.Add(new ClipboardItemViewModel(MakeItem(1, "copy paste"), "copy paste"));
        vm.Items.Add(new ClipboardItemViewModel(MakeItem(2, "another one"), "another one"));

        vm.SearchText = "paste";

        Assert.Single(vm.FilteredItems);
    }

    // ── FilteredItems ordering ────────────────────────────────────────────────

    [Fact]
    public void FilteredItems_PinnedItemsAlwaysFirst()
    {
        var (vm, _, _) = CreateSut();
        vm.Items.Add(new ClipboardItemViewModel(MakeItem(1, "regular item"), "regular item"));
        vm.Items.Add(new ClipboardItemViewModel(MakeItem(2, "pinned item", pinned: true), "pinned item"));
        vm.Items.Add(new ClipboardItemViewModel(MakeItem(3, "another regular"), "another regular"));

        vm.SearchText = string.Empty; // trigger refresh

        Assert.True(vm.FilteredItems[0].IsPinned, "First item must be pinned");
        Assert.Equal("pinned item", vm.FilteredItems[0].DecryptedContent);
    }

    [Fact]
    public void FilteredItems_MultiplePinnedItems_AllAppearBeforeUnpinned()
    {
        var (vm, _, _) = CreateSut();
        vm.Items.Add(new ClipboardItemViewModel(MakeItem(1, "unpinned"), "unpinned"));
        vm.Items.Add(new ClipboardItemViewModel(MakeItem(2, "pinned A", pinned: true), "pinned A"));
        vm.Items.Add(new ClipboardItemViewModel(MakeItem(3, "pinned B", pinned: true), "pinned B"));

        vm.SearchText = string.Empty;

        Assert.True(vm.FilteredItems[0].IsPinned);
        Assert.True(vm.FilteredItems[1].IsPinned);
        Assert.False(vm.FilteredItems[2].IsPinned);
    }

    // ── OnClipboardChanged ────────────────────────────────────────────────────

    [Fact]
    public async Task OnClipboardChanged_NewContent_TriggersLoadAsync()
    {
        // Fix #5: OnClipboardChanged now delegates to LoadAsync so items carry their
        // real DB id. The test verifies GetAllAsync is called (the reload fires).
        var newItem = MakeItem(42, "new content");
        var (vm, storage, _) = CreateSut(new[] { newItem });

        vm.OnClipboardChanged("new content");

        // Allow the fire-and-forget LoadAsync to complete.
        await Task.Delay(50);
        storage.Verify(s => s.GetAllAsync(), Times.AtLeastOnce);
        Assert.Single(vm.Items);
        Assert.Equal("new content", vm.Items[0].DecryptedContent);
    }

    [Fact]
    public async Task OnClipboardChanged_DuplicateContent_LoadsFromStorage()
    {
        // Deduplication is now handled by the storage layer (DB has one canonical row).
        // The ViewModel simply reloads; the test verifies the reload fires.
        var items = new[]
        {
            MakeItem(1, "first"),
            MakeItem(2, "duplicate"),
            MakeItem(3, "third"),
        };
        var (vm, storage, _) = CreateSut(items);

        vm.OnClipboardChanged("duplicate");

        await Task.Delay(50);
        storage.Verify(s => s.GetAllAsync(), Times.AtLeastOnce);
        Assert.Equal(3, vm.Items.Count);
    }

    [Fact]
    public void OnClipboardChanged_EmptyString_DoesNotAddItem()
    {
        var (vm, _, _) = CreateSut();

        vm.OnClipboardChanged(string.Empty);

        Assert.Empty(vm.Items);
    }

    [Fact]
    public void OnClipboardChanged_NullString_DoesNotAddItem()
    {
        var (vm, _, _) = CreateSut();

        vm.OnClipboardChanged(null!);

        Assert.Empty(vm.Items);
    }

    // ── DeleteItemCommand ─────────────────────────────────────────────────────

    [Fact]
    public void DeleteItemCommand_RemovesItemFromCollection()
    {
        var (vm, storage, _) = CreateSut();
        storage.Setup(s => s.DeleteAsync(It.IsAny<long>())).Returns(Task.CompletedTask);

        var item = new ClipboardItemViewModel(MakeItem(1, "to delete"), "to delete");
        vm.Items.Add(item);

        vm.DeleteItemCommand.Execute(item);

        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task DeleteItemCommand_ItemWithStorageId_CallsStorageDelete()
    {
        var (vm, storage, _) = CreateSut();
        storage.Setup(s => s.DeleteAsync(42L)).Returns(Task.CompletedTask);

        var item = new ClipboardItemViewModel(MakeItem(42, "stored item"), "stored item");
        vm.Items.Add(item);

        vm.DeleteItemCommand.Execute(item);

        // Give the fire-and-forget a chance to run.
        await Task.Delay(50);
        storage.Verify(s => s.DeleteAsync(42L), Times.Once);
    }

    [Fact]
    public void DeleteItemCommand_NullItem_DoesNotThrow()
    {
        var (vm, _, _) = CreateSut();
        var ex = Record.Exception(() => vm.DeleteItemCommand.Execute(null));
        Assert.Null(ex);
    }

    // ── SelectItemCommand ─────────────────────────────────────────────────────

    [Fact]
    public async Task SelectItemCommand_CallsPasteService()
    {
        var (vm, _, paste) = CreateSut();
        paste.Setup(p => p.PasteAsync("selected content")).Returns(Task.CompletedTask);

        var item = new ClipboardItemViewModel(MakeItem(1, "selected content"), "selected content");
        vm.Items.Add(item);

        vm.SelectItemCommand.Execute(item);

        // Give the async fire-and-forget time to complete.
        await Task.Delay(200);
        paste.Verify(p => p.PasteAsync("selected content"), Times.Once);
    }

    [Fact]
    public async Task SelectItemCommand_NullItem_DoesNotCallPaste()
    {
        var (vm, _, paste) = CreateSut();

        vm.SelectItemCommand.Execute(null);

        await Task.Delay(50);
        paste.Verify(p => p.PasteAsync(It.IsAny<string>()), Times.Never);
    }

    // ── ClearAllCommand ───────────────────────────────────────────────────────

    [Fact]
    public void ClearAllCommand_RemovesNonPinnedItems()
    {
        var (vm, storage, _) = CreateSut();
        storage.Setup(s => s.DeleteAllAsync(true)).Returns(Task.CompletedTask);

        vm.Items.Add(new ClipboardItemViewModel(MakeItem(1, "unpinned"), "unpinned"));
        vm.Items.Add(new ClipboardItemViewModel(MakeItem(2, "pinned", pinned: true), "pinned"));

        vm.ClearAllCommand.Execute(null);

        // Only the pinned item should remain.
        Assert.Single(vm.Items);
        Assert.True(vm.Items[0].IsPinned);
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    [Fact]
    public void SearchText_SetValue_RaisesPropertyChanged()
    {
        var (vm, _, _) = CreateSut();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SearchText = "new search";

        Assert.Contains(nameof(vm.SearchText), raised);
        Assert.Contains(nameof(vm.FilteredItems), raised);
    }

    [Fact]
    public void SelectedItem_SetValue_RaisesPropertyChanged()
    {
        var (vm, _, _) = CreateSut();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var item = new ClipboardItemViewModel(MakeItem(1, "test"), "test");
        vm.Items.Add(item);
        vm.SelectedItem = item;

        Assert.Contains(nameof(vm.SelectedItem), raised);
    }

    // ── LoadAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_PopulatesItemsFromStorage()
    {
        var storageItems = new List<ClipboardItem>
        {
            MakeItem(1, "item one"),
            MakeItem(2, "item two")
        };

        var (vm, _, _) = CreateSut(storageItems);
        await vm.LoadAsync();

        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public async Task LoadAsync_ClearsExistingItemsBeforeLoading()
    {
        var storageItems = new List<ClipboardItem> { MakeItem(1, "fresh") };
        var (vm, _, _) = CreateSut(storageItems);

        // Seed with stale data.
        vm.Items.Add(new ClipboardItemViewModel(MakeItem(99, "stale"), "stale"));

        await vm.LoadAsync();

        Assert.Single(vm.Items);
        Assert.Equal("fresh", vm.Items[0].DecryptedContent);
    }
}
