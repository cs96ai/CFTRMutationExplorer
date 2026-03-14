using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CftrMutationExplorer.Core.Interfaces;
using CftrMutationExplorer.Core.Models;

namespace CftrMutationExplorer.App.ViewModels;

public partial class AnnotationListViewModel : ObservableObject
{
    private readonly IAnnotationRepository _repository;

    [ObservableProperty]
    private ObservableCollection<Annotation> _annotations = new();

    [ObservableProperty]
    private Annotation? _selectedAnnotation;

    [ObservableProperty]
    private string _newTitle = string.Empty;

    [ObservableProperty]
    private string _newNote = string.Empty;

    [ObservableProperty]
    private AnnotationCategory _newCategory = AnnotationCategory.GeneralNote;

    [ObservableProperty]
    private int? _newTargetResidueNumber;

    [ObservableProperty]
    private string? _newTargetChainId;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public ObservableCollection<AnnotationCategory> AvailableCategories { get; } = new(
        Enum.GetValues<AnnotationCategory>()
    );

    public AnnotationListViewModel(IAnnotationRepository repository)
    {
        _repository = repository;
    }

    [RelayCommand]
    private async Task LoadAnnotations()
    {
        try
        {
            var list = await _repository.GetAllAsync();
            Annotations.Clear();
            foreach (var a in list)
                Annotations.Add(a);
            StatusText = $"{Annotations.Count} annotation(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddAnnotation()
    {
        if (string.IsNullOrWhiteSpace(NewTitle))
        {
            StatusText = "Title is required";
            return;
        }

        var annotation = new Annotation
        {
            Title = NewTitle,
            Note = NewNote,
            Category = NewCategory,
            TargetResidueNumber = NewTargetResidueNumber,
            TargetChainId = string.IsNullOrEmpty(NewTargetChainId) ? null : NewTargetChainId[0],
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            var id = await _repository.AddAsync(annotation);
            annotation.Id = id;
            Annotations.Insert(0, annotation);

            NewTitle = string.Empty;
            NewNote = string.Empty;
            NewCategory = AnnotationCategory.GeneralNote;
            NewTargetResidueNumber = null;
            NewTargetChainId = null;

            StatusText = $"Added: {annotation.Title}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteAnnotation()
    {
        if (SelectedAnnotation == null) return;

        try
        {
            await _repository.DeleteAsync(SelectedAnnotation.Id);
            Annotations.Remove(SelectedAnnotation);
            StatusText = "Annotation deleted";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveEdit()
    {
        if (SelectedAnnotation == null) return;

        try
        {
            SelectedAnnotation.ModifiedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(SelectedAnnotation);
            IsEditing = false;
            StatusText = "Annotation updated";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void StartEdit()
    {
        if (SelectedAnnotation != null)
            IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    public void SetTargetFromSelection(int? residueNumber, char? chainId)
    {
        NewTargetResidueNumber = residueNumber;
        NewTargetChainId = chainId?.ToString();
    }
}
