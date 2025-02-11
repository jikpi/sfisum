using sfisum.Engine.Modes;
using sfisum.Engine.Modes.Base;

namespace sfisum.Engine;

public class ConsoleUi
{
    public void Launch()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Welcome to sfisum.\n");
        Console.ResetColor();

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Select option:");
            Console.ResetColor();

            Console.WriteLine(
                "1) Generate\n" +
                "2) Validate\n" +
                "3) Fast Refresh\n" +
                "4) Full Refresh\n" +
                "5) Find duplicates\n" +
                "6) Exit\n"
            );

            if (!byte.TryParse(Console.ReadLine(), out byte input))
            {
                PrintError("Invalid input. Please enter a number.\n");
                continue;
            }

            if (input == 6)
                break;

            switch (input)
            {
                case 1:
                    GenerateCui();
                    break;
                case 2:
                    ValidateCui();
                    break;
                case 3:
                    RefreshCui(true);
                    break;
                case 4:
                    RefreshCui(false);
                    break;
                case 5:
                    FindDuplicatesCui();
                    break;
                default:
                    PrintError("Invalid input. Please enter a valid number.\n");
                    break;
            }
        }
    }

    private void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private void SaveDigestPrompt(ModeInstanceBase instance)
    {
        bool success = false;
        while (!success)
        {
            Console.WriteLine(
                "Enter the path to save the digest file, leave empty to save in app directory, or 'd' to discard:");
            string? path = Console.ReadLine();

            switch (path)
            {
                case null:
                    PrintError("Invalid input.\n");
                    continue;
                case "d":
                    return;
            }

            success = instance.SaveDigest(path);
            if (!success)
            {
                PrintError("Failed to save digest file. Please try again.\n");
                continue;
            }


            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Digest file saved successfully.\n");
            Console.ResetColor();
        }
    }

    private void GeneralEventPrompt(ModeInstanceBase instance)
    {
        Console.WriteLine(
            "There are events that occurred during hashing. Press enter to view them, or 'd' to discard (Logged still if ON).\n");

        string? input = Console.ReadLine();
        bool toConsole = input != "d";

        instance.PrintGeneralEvents(toConsole);
    }

    private void GenerateCui()
    {
        Console.WriteLine("Enter the path to the directory you want to generate a digest for:");
        string? input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            PrintError("Invalid input. Please enter a valid path.\n");
            return;
        }

        Console.WriteLine("Path loaded. Press enter to start.");
        Console.ReadLine();

        ModeInstanceGenerate instance = new(input);
        try
        {
            instance.Start();
        }
        catch (Exception e)
        {
            PrintError($"Fatal Error: {e.Message}\n");
            return;
        }

        if (instance.HasInaccessibleFiles || instance.HasHashErrors)
        {
            GeneralEventPrompt(instance);
        }

        if (instance.SuccesfullyHashedFiles <= 0)
        {
            PrintError("No files were successfully hashed. Cannot save digest.\n");
            return;
        }

        SaveDigestPrompt(instance);
    }

    private void ValidateCui()
    {
        Console.WriteLine("Enter the path to the base directory:");
        string? baseDirPath = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(baseDirPath))
        {
            PrintError("Invalid input. Please enter a valid path.\n");
            return;
        }

        Console.WriteLine("Enter the path to the existing digest file:");

        string? digestPath = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(digestPath))
        {
            PrintError("Invalid input. Please enter a valid path.\n");
            return;
        }

        Console.WriteLine("Paths loaded. Press enter to start.");
        Console.ReadLine();

        ModeInstanceValidate instance = new(baseDirPath, digestPath);

        try
        {
            instance.Start();
        }
        catch (Exception e)
        {
            PrintError($"Fatal Error: {e.Message}\n");
            return;
        }

        if (instance.HasEvents)
        {
            GeneralEventPrompt(instance);
        }

        if (instance.SuccesfullyHashedFiles <= 0)
        {
            PrintError("No files were successfully hashed. Cannot save digest.\n");
            return;
        }

        SaveDigestPrompt(instance);
    }

    private void RefreshCui(bool fastMode)
    {
        Console.WriteLine("Enter the path to the base directory:");
        string? baseDirPath = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(baseDirPath))
        {
            PrintError("Invalid input. Please enter a valid path.\n");
            return;
        }

        Console.WriteLine("Enter the path to the existing digest file:");

        string? digestPath = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(digestPath))
        {
            PrintError("Invalid input. Please enter a valid path.\n");
            return;
        }

        Console.WriteLine("Paths loaded. Press enter to start.");
        Console.ReadLine();

        ModeInstanceRefreshBase instance;

        if (fastMode)
        {
            instance = new ModeInstanceFastRefresh(baseDirPath, digestPath);
        }
        else
        {
            instance = new ModeInstanceFullRefresh(baseDirPath, digestPath);
        }

        try
        {
            instance.Start();
        }
        catch (Exception e)
        {
            PrintError($"Fatal Error: {e.Message}\n");
            return;
        }

        if (instance.GetEventCount() > 0)
        {
            GeneralEventPrompt(instance);
        }

        if (instance.TotalToSave > 0)
        {
            SaveDigestPrompt(instance);
        }
    }

    private void FindDuplicatesCui()
    {
        Console.WriteLine("Enter the path to the existing digest file:");

        string? digestPath = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(digestPath))
        {
            PrintError("Invalid input. Please enter a valid path.\n");
            return;
        }

        Console.WriteLine("Path loaded. Press enter to start.");
        Console.ReadLine();

        ModeInstanceDuplicates instance = new(digestPath);

        try
        {
            instance.Start();
        }
        catch (Exception e)
        {
            PrintError($"Fatal Error: {e.Message}\n");
            return;
        }

        if (instance.DuplicatesCount > 0)
        {
            GeneralEventPrompt(instance);
        }
    }
}