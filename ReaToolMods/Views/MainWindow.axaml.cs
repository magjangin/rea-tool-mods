using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using ReaToolMods.ViewModels;

namespace ReaToolMods.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var files = e.DataTransfer.TryGetFiles();
        if (files == null || files.Length == 0) return;

        var first = files[0].Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(first))
        {
            await vm.HandleDroppedPathAsync(first);
        }
    }
}