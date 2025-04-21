namespace sfisum.Engine.Modes.Base;

internal abstract class ModeInstanceBase
{
    public abstract void Start();
    public abstract bool SaveDigest(string path, string? digestFilenamePrefix);
    public abstract void PrintGeneralEvents(bool toConsole);
}