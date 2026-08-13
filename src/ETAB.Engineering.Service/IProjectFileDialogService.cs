namespace ETAB.Engineering.Service;

public interface IProjectFileDialogService
{
    Task<string?> SelectOpenProjectAsync(CancellationToken cancellationToken);

    Task<string?> SelectSaveProjectAsync(
        string suggestedFileName,
        CancellationToken cancellationToken);
}
