namespace DesktopPet.Core;

public enum CharacterImportPhase { Preparing, Extracting, Copying, Validating }
public readonly record struct CharacterImportProgress(CharacterImportPhase Phase, int CompletedFiles, int TotalFiles,
    long CopiedBytes, long TotalBytes);
