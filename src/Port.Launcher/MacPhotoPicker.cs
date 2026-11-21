using Port.Host;

internal sealed class MacPhotoPicker(string program) : IGamePhotoPicker
{
    public async Task<IReadOnlyList<string>> PickPhotosAsync(int maximumCount,
        CancellationToken cancellationToken)
    {
        var selected = await MacDialogs.ShowAsync(program, cancellationToken, "photos");
        return selected is null ? [] : selected.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Take(maximumCount).ToArray();
    }
}
