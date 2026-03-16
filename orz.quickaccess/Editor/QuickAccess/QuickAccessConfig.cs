using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Universal.QuickAccess
{
    [Serializable]
    public class QuickAccessMenuItem
    {
        public string displayName;
        public string menuPath;
        public string groupName;
    }

    [Serializable]
    public class QuickAccessConfigData
    {
        public List<QuickAccessMenuItem> items = new List<QuickAccessMenuItem>();
        public bool followMouse = false;
        public string anchorDirection = "TopLeft";
        public bool pinned = false;
    }

    public static class QuickAccessConfig
    {
        private const string JsonPath = "UserSettings/QuickAccessConfig.json";

        private static QuickAccessConfigData _instance;

        public static QuickAccessConfigData Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Load();
                return _instance;
            }
        }

        public static string FullPath => Path.GetFullPath(JsonPath);

        private static QuickAccessConfigData Load()
        {
            if (File.Exists(FullPath))
            {
                try
                {
                    var json = File.ReadAllText(FullPath);
                    var data = JsonUtility.FromJson<QuickAccessConfigData>(json);
                    if (data != null && data.items != null)
                    {
                        if (string.IsNullOrEmpty(data.anchorDirection))
                            data.anchorDirection = "TopLeft";
                        return data;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"QuickAccess: Failed to load config: {e.Message}");
                }
            }

            // Create default
            var defaultData = new QuickAccessConfigData
            {
                items = new List<QuickAccessMenuItem>
                {
                    new QuickAccessMenuItem { displayName = "Frame Debugger", menuPath = "Window/Analysis/Frame Debugger", groupName = "Windows" },
                    new QuickAccessMenuItem { displayName = "Profiler", menuPath = "Window/Analysis/Profiler %7", groupName = "Windows" },
                }
            };
            Save(defaultData);
            return defaultData;
        }

        public static void Save(QuickAccessConfigData data = null)
        {
            data = data ?? _instance;
            if (data == null) return;

            try
            {
                var dir = Path.GetDirectoryName(FullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonUtility.ToJson(data, true);
                File.WriteAllText(FullPath, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"QuickAccess: Failed to save config: {e.Message}");
            }
        }

        public static void Reload()
        {
            _instance = null;
        }
    }
}
