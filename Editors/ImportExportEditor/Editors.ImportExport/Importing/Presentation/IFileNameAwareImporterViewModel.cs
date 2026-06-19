using Shared.Core.PackFiles.Models;

namespace Editors.ImportExport.Importing.Presentation;

public interface IFileNameAwareImporterViewModel
{
    void ConfigureFromInputFile(PackFile file);
}
