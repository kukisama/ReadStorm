using System.Text.Json;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Infrastructure.Services;

public sealed class JsonFileBookshelfUseCase : IBookshelfUseCase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _filePath;

    public JsonFileBookshelfUseCase(string? filePath = null)
    {
        _filePath = filePath ?? ResolveDefaultPath();
    }

    public async Task<IReadOnlyList<BookRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_filePath);
        var books = await JsonSerializer.DeserializeAsync<List<BookRecord>>(stream, JsonOptions, cancellationToken);
        return books ?? [];
    }

    public async Task AddAsync(BookRecord book, CancellationToken cancellationToken = default)
    {
        var books = (await GetAllAsync(cancellationToken)).ToList();
        books.Add(book);
        await SaveAsync(books, cancellationToken);
    }

    public async Task RemoveAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        var books = (await GetAllAsync(cancellationToken)).ToList();
        books.RemoveAll(b => b.Id == bookId);
        await SaveAsync(books, cancellationToken);
    }

    public async Task UpdateProgressAsync(Guid bookId, ReadingProgress progress, CancellationToken cancellationToken = default)
    {
        var books = (await GetAllAsync(cancellationToken)).ToList();
        var book = books.FirstOrDefault(b => b.Id == bookId);
        if (book is not null)
        {
            book.Progress = progress;
            await SaveAsync(books, cancellationToken);
        }
    }

    private async Task SaveAsync(List<BookRecord> books, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(folder);

        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, books, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }

    private static string ResolveDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "ReadStorm");
        return Path.Combine(dir, "bookshelf.json");
    }
}
