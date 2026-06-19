namespace Editors.ImportExport.Common.FbxSdk;

public sealed class FbxExportNotSupportedException : NotSupportedException
{
    public FbxExportNotSupportedException(string message)
        : base(message)
    {
    }
}
