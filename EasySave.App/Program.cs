using EasySave.App.Controllers;
using EasySave.App.Views;
using EasySave.App.Enumerations;
using System;

namespace EasySave.App
{
    class Program
    {
        static void Main(string[] args)
        {
            BackupController controller = new BackupController();
            ConsoleView view = new ConsoleView();

            bool running = true;

            Console.WriteLine(Config.ResourceSettings.GetString("Welcome"));

            while (running)
            {
                view.DisplayMenu();
                string choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        var jobs = controller.GetJobs();
                        view.DisplayJobs(jobs);
                        break;

                    case "2":
                        try
                        {
                            string name = view.AskForInput("EnterName");
                            string source = view.AskForInput("EnterSource");
                            string target = view.AskForInput("EnterTarget");
                            BackupType type = view.AskForBackupType();

                            controller.CreateJob(name, source, target, type);
                            view.DisplaySuccess();
                        }
                        catch (Exception ex)
                        {
                            view.DisplayError(ex.Message);
                        }
                        break;

                    case "3":
                        view.DisplayJobs(controller.GetJobs());
                        string indexStr = view.AskForInput("EnterName");

                        if (int.TryParse(indexStr, out int index))
                        {
                            controller.ExecuteJob(index);
                            view.DisplaySuccess();
                        }
                        else
                        {
                            view.DisplayError("Invalid index.");
                        }
                        break;


                    case "4":
                        view.DisplayJobs(controller.GetJobs());
                        string deleteIndexStr = view.AskForInput("EnterName");

                        if (int.TryParse(deleteIndexStr, out int deleteIndex))
                        {
                            controller.DeleteJob(deleteIndex);

                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine(Config.ResourceSettings.GetString("JobDeleted"));
                            Console.ResetColor();
                        }
                        else
                        {
                            view.DisplayError("Invalid index.");
                        }
                        break;

                    case "5":
                        Console.WriteLine(Config.ResourceSettings.GetString("ConfigMenu"));
                        string configChoice = Console.ReadLine();

                        if (configChoice == "1")
                        {
                            string langChoice = view.AskForInput("ChangeLang");
                            if (langChoice.ToLower() == "fr") Config.AppSettings.Instance.Language = Language.Francais;
                            else if (langChoice.ToLower() == "en") Config.AppSettings.Instance.Language = Language.English;
                        }
                        else if (configChoice == "2")
                        {
                            string formatChoice = view.AskForInput("ChangeFormat");

                            if (formatChoice.ToLower() == "xml")
                            {
                                Config.AppSettings.Instance.LogFormat = "xml";
                                controller.UpdateLogger();
                                Console.WriteLine(Config.ResourceSettings.GetString("FormatChanged"));
                            }
                            else if (formatChoice.ToLower() == "json")
                            {
                                Config.AppSettings.Instance.LogFormat = "json";
                                controller.UpdateLogger();
                                Console.WriteLine(Config.ResourceSettings.GetString("FormatChanged"));
                            }
                            else
                            {
                                view.DisplayError("Invalid format.");
                            }
                        }

                        Console.Clear();
                        break;

                    case "6":
                        running = false;
                        break;

                    default:
                        Console.WriteLine("Invalid choice.");
                        break;
                }
            }
        }
    }
}