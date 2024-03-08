using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Damon.Command
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class CommandAttribute : Attribute
    {
        public string CommandName { get; }
        public string HelpText { get; }

        public CommandAttribute(string commandName = "", string helpText = "")
        {
            CommandName = commandName;
            HelpText = helpText;
        }
    }

    public class CommandRegistry
    {
        private static readonly Dictionary<Type, List<MethodInfo>> CommandMethodCache = new();
        private readonly Dictionary<string, MethodInfo> _commandMethods = new();
        private readonly object _commandTarget;

        public CommandRegistry(object target)
        {
            _commandTarget = target ?? throw new ArgumentNullException(nameof(target), "Target cannot be null.");
            DiscoverCommands();
        }

        private void DiscoverCommands()
        {
            Type targetType = _commandTarget.GetType();

            if (!CommandMethodCache.TryGetValue(targetType, out var methods))
            {
                methods = targetType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                                    .Where(m => m.GetCustomAttributes(typeof(CommandAttribute), true).Any())
                                    .ToList();
                CommandMethodCache[targetType] = methods;
            }

            foreach (MethodInfo method in methods)
            {
                CommandAttribute commandAttr = method.GetCustomAttribute<CommandAttribute>(true);
                string commandName = string.IsNullOrEmpty(commandAttr.CommandName) ? method.Name.ToLower() : commandAttr.CommandName.ToLower();

                if (_commandMethods.ContainsKey(commandName))
                {
                    Debug.LogError($"Duplicate command name detected: {commandName}. Command names must be unique.");
                    continue;
                }

                _commandMethods.Add(commandName, method);
            }
        }

        public List<string> GetAllCommandNames() => _commandMethods.Keys.ToList();

        public async Task<bool> ExecuteCommand(string commandName, string[] args)
        {
            if (_commandMethods.TryGetValue(commandName.ToLower(), out MethodInfo method))
            {
                try
                {
                    if (method.ReturnType == typeof(Task))
                    {
                        await (Task)method.Invoke(_commandTarget, new object[] { args });
                    }
                    else
                    {
                        method.Invoke(_commandTarget, new object[] { args });
                    }
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error executing command '{commandName}': {e.Message}");
                    return false;
                }
            }
            return false;
        }
    }

    public class CommandExecutor
    {
        private readonly CommandRegistry _commandRegistry;
        private readonly UIController _uiController;

        public CommandExecutor(CommandRegistry registry, UIController uiController)
        {
            _commandRegistry = registry ?? throw new ArgumentNullException(nameof(registry), "Registry cannot be null.");
            _uiController = uiController ?? throw new ArgumentNullException(nameof(uiController), "UIController cannot be null.");
        }

        public async Task ExecuteCommand(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                _uiController.UpdateLog("Error: Command input is empty or null.");
                return;
            }

            ParseInput(input, out string commandName, out string[] args);

            bool result = await _commandRegistry.ExecuteCommand(commandName, args);
            if (!result)
            {
                _uiController.UpdateLog($"Error: Command '{commandName}' not found or failed to execute.");
            }
            else
            {
                _uiController.UpdateLog($"Command '{commandName}' executed successfully.");
            }
        }

        private void ParseInput(string input, out string commandName, out string[] args)
        {
            var segments = input.Split(' ')
                                 .Where(segment => !string.IsNullOrWhiteSpace(segment))
                                 .ToArray();

            commandName = segments.FirstOrDefault()?.ToLower() ?? string.Empty;
            args = segments.Skip(1).ToArray();
        }
    }

    public class AutoCompleteSystem
    {
        private readonly CommandRegistry _commandRegistry;

        public AutoCompleteSystem(CommandRegistry registry)
        {
            _commandRegistry = registry;
        }

        public string[] GetSuggestions(string input)
        {
            var parts = input.Split(' ');
            if (parts.Length == 0) return Array.Empty<string>();

            string currentCommand = parts[0];
            return _commandRegistry.GetAllCommandNames()
                                   .Where(name => name.StartsWith(currentCommand, StringComparison.CurrentCultureIgnoreCase))
                                   .ToArray();
        }
    }

    public class UIController : MonoBehaviour
    {
        public TMP_InputField InputField;
        public TMP_Text SuggestionsText;
        public TMP_Text OutputLog;
        public GameObject ConsolePanel;
        public float FadeDuration = 0.5f;

        private CommandExecutor _commandExecutor;
        private CanvasGroup _consoleCanvasGroup;
        private AutoCompleteSystem _autoCompleteSystem;


        private List<string> _commandHistory = new List<string>();
        private int _commandHistoryIndex = -1;

        private StringBuilder logBuilder = new StringBuilder();



        void Start()
        {
            var commandRegistry = new CommandRegistry(this);
            _commandExecutor = new CommandExecutor(commandRegistry, this);
            _autoCompleteSystem = new AutoCompleteSystem(commandRegistry);

            if (!ConsolePanel.TryGetComponent<CanvasGroup>(out _consoleCanvasGroup))
            {
                _consoleCanvasGroup = ConsolePanel.AddComponent<CanvasGroup>();
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.BackQuote))
            {
                ToggleConsole();
            }

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                string input = InputField.text;
                string[] suggestions = _autoCompleteSystem.GetSuggestions(input);

                SuggestionsText.text = string.Join(", ", suggestions);
            }

            if (Input.GetKeyDown(KeyCode.Return) && ConsolePanel.activeSelf)
            {
                SubmitCommand(InputField.text);
                _commandHistory[_commandHistory.Count - 1] = InputField.text;
                _commandHistory.Add(string.Empty);
                _commandHistoryIndex = _commandHistory.Count - 1;
                InputField.text = string.Empty;
                InputField.ActivateInputField();
            }

            if (ConsolePanel.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    if (_commandHistory.Count > 0 && _commandHistoryIndex > 0)
                    {
                        _commandHistoryIndex--;
                        InputField.text = _commandHistory[_commandHistoryIndex];
                        InputField.caretPosition = InputField.text.Length;
                    }
                }
                else if (Input.GetKeyDown(KeyCode.DownArrow))
                {
                    if (_commandHistoryIndex < _commandHistory.Count - 1)
                    {
                        _commandHistoryIndex++;
                        InputField.text = _commandHistory[_commandHistoryIndex];
                        InputField.caretPosition = InputField.text.Length;
                    }
                }
            }
        }

        void ToggleConsole()
        {
            bool shouldBeVisible = !ConsolePanel.activeSelf;
            StartCoroutine(AnimateConsoleVisibility(shouldBeVisible));
        }

        IEnumerator AnimateConsoleVisibility(bool visible)
        {
            float elapsedTime = 0;
            float start = _consoleCanvasGroup.alpha;
            float end = visible ? 1 : 0;

            ConsolePanel.SetActive(true);
            _consoleCanvasGroup.blocksRaycasts = visible;

            while (elapsedTime < FadeDuration)
            {
                elapsedTime += Time.deltaTime;
                _consoleCanvasGroup.alpha = Mathf.Lerp(start, end, elapsedTime / FadeDuration);
                yield return null;
            }

            if (!visible)
            {
                ConsolePanel.SetActive(false);
            }
            else
            {
                InputField.ActivateInputField();
            }
        }

        async void SubmitCommand(string command)
        {
            await _commandExecutor.ExecuteCommand(command);
        }


        public void UpdateLog(string message)
        {
            logBuilder.AppendLine(message);
            OutputLog.text = logBuilder.ToString();
        }
    }
}
