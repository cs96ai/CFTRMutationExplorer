using CftrMutationExplorer.Core.Models;
using CftrMutationExplorer.Infrastructure.Persistence;

namespace CftrMutationExplorer.Tests;

public class AnnotationRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAnnotationRepository _repo;

    public AnnotationRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"cftr_test_{Guid.NewGuid():N}.db");
        DatabaseInitializer.EnsureCreated(_dbPath);
        _repo = new SqliteAnnotationRepository(_dbPath);
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task CanAddAndRetrieveAnnotation()
    {
        var annotation = new Annotation
        {
            Title = "Test Mutation Site",
            Note = "Interesting folding change",
            Category = AnnotationCategory.MutationImpact,
            TargetChainId = 'A',
            TargetResidueNumber = 508
        };

        var id = await _repo.AddAsync(annotation);
        Assert.True(id > 0);

        var retrieved = await _repo.GetByIdAsync(id);
        Assert.NotNull(retrieved);
        Assert.Equal("Test Mutation Site", retrieved!.Title);
        Assert.Equal(AnnotationCategory.MutationImpact, retrieved.Category);
        Assert.Equal(508, retrieved.TargetResidueNumber);
    }

    [Fact]
    public async Task CanListAllAnnotations()
    {
        await _repo.AddAsync(new Annotation { Title = "A1" });
        await _repo.AddAsync(new Annotation { Title = "A2" });
        await _repo.AddAsync(new Annotation { Title = "A3" });

        var all = await _repo.GetAllAsync();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task CanUpdateAnnotation()
    {
        var id = await _repo.AddAsync(new Annotation { Title = "Original" });
        var annotation = await _repo.GetByIdAsync(id);
        annotation!.Title = "Updated";
        annotation.Note = "New note";

        await _repo.UpdateAsync(annotation);

        var updated = await _repo.GetByIdAsync(id);
        Assert.Equal("Updated", updated!.Title);
        Assert.Equal("New note", updated.Note);
        Assert.NotNull(updated.ModifiedAt);
    }

    [Fact]
    public async Task CanDeleteAnnotation()
    {
        var id = await _repo.AddAsync(new Annotation { Title = "To Delete" });
        await _repo.DeleteAsync(id);

        var deleted = await _repo.GetByIdAsync(id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task CanFilterByResidue()
    {
        await _repo.AddAsync(new Annotation { Title = "At 508", TargetChainId = 'A', TargetResidueNumber = 508 });
        await _repo.AddAsync(new Annotation { Title = "At 509", TargetChainId = 'A', TargetResidueNumber = 509 });
        await _repo.AddAsync(new Annotation { Title = "Also 508", TargetChainId = 'A', TargetResidueNumber = 508 });

        var result = await _repo.GetByResidueAsync('A', 508);
        Assert.Equal(2, result.Count);
    }
}
