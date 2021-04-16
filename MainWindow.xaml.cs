using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Windows;
using System.Text.Json;
using System.IO;
using System.Diagnostics;
using System.Windows.Controls;
using System.Linq;

namespace PowerTweaks
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private const string RegistryDirectory = @"SOFTWARE\Classes\Directory\";
        private readonly List<ContextItem> _items;
        private int _itemIndex;
        private const int ButtonHeight = 25;
        private const int ButtonMargin = 10;

        public MainWindow()
        {
            InitializeComponent();

            _items = GetItems();

            foreach (var item in _items)
            {
                AddItemToGrid(item, Button_Click_AddContext);
            }

            AddItemToGrid(new ContextItem { KeyPath = "Add all to context menu"}, (_, _) =>
            {
                AddAllMenus();
            });

            AddItemToGrid(new ContextItem { KeyPath = "Remove all from context menu" }, (_, _) =>
            {
                RemoveAllChanges();
            });

            AddItemToGrid(new ContextItem { KeyPath = "Exit" }, (_, _) =>
            {
                AppWindow.Close();
            });

            if (!CheckAdminRights())
            {
                MessageBox.Show("not admin");
                //IsAdminText.Visibility = Visibility.Visible;
            }
        }

        private void AddItemToGrid(ContextItem itemToAdd, RoutedEventHandler clickEvent)
        {
            if (itemToAdd.Separator)
            {
                AddSeparatorToGrid();
                return;
            }

            AppWindow.Height += ButtonHeight + ButtonMargin;

            ButtonGrid.RowDefinitions.Add(CreateNewDefinition());

            var button = new Button
            {
                Height = ButtonHeight,
                Margin = new Thickness { Bottom = ButtonMargin },
                Content = itemToAdd.KeyPath
            };

            button.Click += clickEvent;
            ButtonGrid.Children.Add(button);
            Grid.SetRow(button, _itemIndex);
            _itemIndex++;
        }

        private void AddSeparatorToGrid()
        {
            ButtonGrid.RowDefinitions.Add(CreateNewDefinition());
            var separator = new Separator();
            ButtonGrid.Children.Add(separator);
            Grid.SetRow(separator, _itemIndex);
            _itemIndex++;
        }

        private static RowDefinition CreateNewDefinition()
        {
            return new() { Height = new GridLength(ButtonHeight + ButtonMargin) };
        }

        private void Button_Click_AddContext(object sender, RoutedEventArgs e)
        {
            var content = ((Button) sender).Content.ToString();
            var menuItems = JsonSerializer.Deserialize<List<ContextItem>>(File.ReadAllText("menus.json"));

            if (menuItems == null)
            {
                throw new NullReferenceException(nameof(menuItems));
            }

            AddItemToContextMenu(menuItems.FirstOrDefault(item => item.KeyPath == content));
        }

        private void AddAllMenus()
        {
            var menuItems = JsonSerializer.Deserialize<List<ContextItem>>(File.ReadAllText("menus.json"));

            if (menuItems == null)
            {
                throw new NullReferenceException(nameof(menuItems));
            }
            
            foreach (var item in menuItems)
            {
                AddItemToContextMenu(item);
            }
        }

        private static List<ContextItem> GetItems()
        {
            return JsonSerializer.Deserialize<List<ContextItem>>(File.ReadAllText("menus.json"));
        }

        public class ContextItem
        {
            public string KeyPath { get; init; }
            public bool IsAdmin { get; init; }
            public PromptType PromptType { get; init; }
            public bool Background  { get; init; }
            public bool Separator { get; init; }
        }

        public enum PromptType
        {
            CommandPrompt = 0,
            PowerShell = 1,
            CommandPromptCommand = 2
        }

        private void AddItemToContextMenu(ContextItem item)
        {
            _ = item ?? throw new ArgumentNullException(nameof(item));

            if (string.IsNullOrEmpty(item.KeyPath))
            {
                return;
            }

            var promptPath = Environment.SystemDirectory;
            string baseString;

            static string ReplaceInString(string baseString, string variable)
            {
                return baseString.Replace("%replace%", variable);
            }

            switch (item.PromptType)
            {
                case PromptType.CommandPrompt:
                    promptPath += @"\cmd.exe";

                    if (item.IsAdmin)
                    {
                        baseString = "PowerShell.exe -windowstyle hidden -Command \"Start-Process cmd.exe -Verb RunAs -ArgumentList @('/s,/k,pushd,%replace%')";
                        break;
                    }

                    baseString = "cmd.exe /K \"cd %replace%\"";
                    break;

                case PromptType.PowerShell:
                    promptPath += @"\WindowsPowerShell\v1.0\powershell.exe";

                    if (item.IsAdmin)
                    {
                        baseString = "PowerShell.exe -Command \"Start-Process PowerShell -Verb RunAs -ArgumentList \"\"\"-NoExit -c Set-Location '%replace%'\"\"\"";
                        break;
                    }

                    baseString = "PowerShell.exe -NoExit -Command \"Set-Location '%replace%'\"";
                    break;

                case PromptType.CommandPromptCommand:
                    promptPath += @"\cmd.exe";
                    baseString = "cmd.exe /K \"rmdir / s / q \"%1\"\" && exit";
                    break;

                default:
                    return;
            }

            var value = ReplaceInString(baseString, "%1");
            var backgroundValue = ReplaceInString(baseString, "%W");

            var pathNormal = RegistryDirectory + "shell\\" + item.KeyPath;
            var pathNormalCommand = pathNormal + "\\command";
            var pathBackground = RegistryDirectory + "Background\\shell\\" + item.KeyPath;
            var pathBackgroundCommand = pathBackground + "\\command";

            File.AppendAllText("log.txt", pathNormalCommand + Environment.NewLine + value + Environment.NewLine);
            File.AppendAllText("log.txt", pathBackgroundCommand + Environment.NewLine + backgroundValue + Environment.NewLine + Environment.NewLine);

            if (RegValueExists(pathNormal, "Icon"))
            {
                return;
            }

            if (RegValueExists(pathBackground, "Icon"))
            {
                return;
            }

            var failed = new List<bool>
            {
                AddItemToRegistry(pathNormal, promptPath, true),
                AddItemToRegistry(pathNormalCommand, value),
                
            };

            if (item.Background)
            {
                failed.Add(AddItemToRegistry(pathBackground, promptPath, true));
                failed.Add(AddItemToRegistry(pathBackgroundCommand, backgroundValue));
            }

            if (failed.Contains(false))
            {
                MessageBox.Show("something went wronk");
            }
        }

        private static bool AddItemToRegistry(string path, string command, bool withIcon = false)
        {
            if (string.IsNullOrEmpty(path))
            {
                return true;
            }

            if (string.IsNullOrEmpty(command))
            {
                return true;
            }

            if (RegKeyExists(path)) return true;
            Debug.WriteLine(path);
            Registry.CurrentUser.CreateSubKey(path);

            _ = withIcon 
                ? WriteIconRegKey(path, command) 
                : WriteRegKey(path, command);

            return true;

        }

        private static bool WriteRegKey(string path, string value)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(value))
            {
                return false;
            }
            
            Registry.CurrentUser.OpenSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree)?.SetValue("", value);
            
            return RegKeyExists(path);
        }
        
        private static bool WriteIconRegKey(string path, string value)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(value))
            {
                return false;
            }
            
            Registry.CurrentUser.OpenSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree)?.SetValue("Icon", value + ",0");
            
            return RegKeyExists(path);
        }

        private void RemoveAllChanges()
        {
            // Remove our keys
            foreach (var item in _items.Where(item => !string.IsNullOrEmpty(item.KeyPath)))
            {
                var pathNormal = RegistryDirectory + "shell\\" + item.KeyPath;
                var pathBackground = RegistryDirectory + "Background\\shell\\" + item.KeyPath;

                if (RegKeyExists(pathNormal))
                {
                    Debug.WriteLine(pathNormal);
                    Registry.CurrentUser.DeleteSubKeyTree(pathNormal);
                }

                if (!RegKeyExists(pathBackground)) continue;
                Debug.WriteLine(pathBackground);
                Registry.CurrentUser.DeleteSubKeyTree(pathBackground);
            }
        }

        private static bool RegKeyExists(string path)
        {
            return Registry.CurrentUser.OpenSubKey(path) != null;
        }

        private static bool RegValueExists(string path, string value)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path));
            }
            
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException(nameof(value));
            }
            
            if (RegKeyExists(path))
            {
                return Registry.CurrentUser.OpenSubKey(path)?.GetValue(value) != null;
            }

            return false;
        }

        private static bool CheckAdminRights()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
