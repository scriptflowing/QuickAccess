using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Universal.QuickAccess
{
    public class QuickAccessHub : EditorWindow
    {
        private Vector2 _scrollPos;
        private QuickAccessConfigData _config;
        private DateTime _lastLoadTime;

        // Cached GUI styles to reduce per-frame GC pressure
        private GUIStyle _groupLabelStyle;
        private GUIStyle _menuItemButtonStyle;

        // Mouse follow positioning
        private bool _pendingReposition;
        private string _pendingAnchorDirection;

        [MenuItem("Window/Quick Access")]
        public static void OpenWindow()
        {
            var config = QuickAccessConfig.Instance;
            var window = GetWindow<QuickAccessHub>("Quick Access");
            window.minSize = new Vector2(200, 200);
            window.Show();
            window.Focus();

            if (config.followMouse)
            {
                window._pendingReposition = true;
                window._pendingAnchorDirection = config.anchorDirection;
            }
        }

        private void OnEnable()
        {
            EnsureConfig();
        }

        private void EnsureConfig()
        {
            var writeTime = System.IO.File.Exists(QuickAccessConfig.FullPath)
                ? System.IO.File.GetLastWriteTime(QuickAccessConfig.FullPath)
                : DateTime.MinValue;

            if (_config == null || writeTime > _lastLoadTime)
            {
                QuickAccessConfig.Reload();
                _config = QuickAccessConfig.Instance;
                _lastLoadTime = writeTime;
            }
        }

        private void OnGUI()
        {
            if (_pendingReposition)
            {
                _pendingReposition = false;
                PositionNearMouse(_pendingAnchorDirection);
            }

            EnsureConfig();
            var config = _config;
            if (config == null) return;

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            var grouped = config.items
                .GroupBy(i => string.IsNullOrEmpty(i.groupName) ? "" : i.groupName)
                .OrderBy(g => string.IsNullOrEmpty(g.Key) ? 1 : 0)
                .ThenBy(g => g.Key);

            foreach (var g in grouped)
            {
                var firstInGroup = true;
                foreach (var item in g.OrderBy(i => i.displayName ?? ""))
                {
                    var group = item.groupName ?? "";
                    var displayName = StripShortcut(!string.IsNullOrEmpty(item.displayName)
                        ? item.displayName
                        : item.menuPath?.Split('/').Last() ?? "???");

                    GUILayout.BeginHorizontal();
                    GUILayout.Space(4);

                    if (firstInGroup && !string.IsNullOrEmpty(group))
                    {
                        GUILayout.Label(group, GroupLabelStyle, GUILayout.Width(60));
                    }
                    else
                    {
                        GUILayout.Label("", EditorStyles.miniLabel, GUILayout.Width(60));
                    }

                    firstInGroup = false;

                    if (GUILayout.Button(displayName, MenuItemButtonStyle, GUILayout.ExpandWidth(true)))
                    {
                        EditorApplication.ExecuteMenuItem(item.menuPath);
                        if (!config.pinned)
                            Close();
                    }

                    GUILayout.Space(4);
                    GUILayout.EndHorizontal();
                    GUILayout.Space(1);
                }
            }

            if (config.items.Count == 0)
            {
                GUILayout.Space(4);
                EditorGUILayout.LabelField("No items. Open Settings to add.", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndScrollView();

            // Bottom bar — pin toggle (left) + settings icon (right)
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            GUILayout.Space(4);

            var newPinned = GUILayout.Toggle(config.pinned, "Pin", GUILayout.Width(40));
            if (newPinned != config.pinned)
            {
                config.pinned = newPinned;
                QuickAccessConfig.Save(config);
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(EditorGUIUtility.IconContent("d_Settings", "Settings"), GUIStyle.none, GUILayout.Width(20), GUILayout.Height(20)))
            {
                _openingSettings = true;
                QuickAccessSetting.OnOpen();
            }

            GUILayout.Space(4);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        private bool _openingSettings;

        private void OnLostFocus()
        {
            EnsureConfig();
            if (_config != null && !_config.pinned && !_openingSettings && !QuickAccessSetting.IsOpen)
                EditorApplication.delayCall += Close;

            _openingSettings = false;
        }

        private void PositionNearMouse(string anchorDirection)
        {
            var mousePos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            var windowSize = position.size;
            var screenWidth = Screen.currentResolution.width;
            var screenHeight = Screen.currentResolution.height;

            Vector2 windowPos;
            switch (anchorDirection)
            {
                case "TopRight":
                    windowPos = new Vector2(mousePos.x - windowSize.x, mousePos.y);
                    break;
                case "BottomLeft":
                    windowPos = new Vector2(mousePos.x, mousePos.y - windowSize.y);
                    break;
                case "BottomRight":
                    windowPos = new Vector2(mousePos.x - windowSize.x, mousePos.y - windowSize.y);
                    break;
                case "Center":
                    windowPos = new Vector2(mousePos.x - windowSize.x / 2, mousePos.y - windowSize.y / 2);
                    break;
                case "TopLeft":
                default:
                    windowPos = new Vector2(mousePos.x, mousePos.y);
                    break;
            }

            windowPos.x = Mathf.Clamp(windowPos.x, 0, screenWidth - windowSize.x);
            windowPos.y = Mathf.Clamp(windowPos.y, 0, screenHeight - windowSize.y);

            position = new Rect(windowPos, windowSize);
        }

        private GUIStyle GroupLabelStyle
        {
            get
            {
                if (_groupLabelStyle == null)
                {
                    _groupLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleLeft,
                        padding = new RectOffset(2, 2, 0, 0)
                    };
                }
                return _groupLabelStyle;
            }
        }

        private GUIStyle MenuItemButtonStyle
        {
            get
            {
                if (_menuItemButtonStyle == null)
                {
                    _menuItemButtonStyle = new GUIStyle(EditorStyles.miniButton)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        padding = new RectOffset(6, 4, 0, 0)
                    };
                }
                return _menuItemButtonStyle;
            }
        }

        public static string StripShortcut(string name)
        {
            var spaceIdx = name.LastIndexOf(' ');
            if (spaceIdx > 0 && spaceIdx < name.Length - 1)
            {
                var suffix = name.Substring(spaceIdx + 1);
                if (suffix.Length > 0 && (suffix[0] == '%' || suffix[0] == '#' || suffix[0] == '&' || suffix[0] == '_'))
                    return name.Substring(0, spaceIdx);
            }
            return name;
        }

        public static string GuessGroup(string menuPath)
        {
            var slashIndex = menuPath.IndexOf('/');
            return slashIndex > 0 ? menuPath.Substring(0, slashIndex) : "";
        }

        public static void OpenShortcutManager()
        {
            EditorApplication.ExecuteMenuItem("Edit/Shortcuts...");
            EditorApplication.delayCall += () =>
            {
                foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                {
                    if (w.GetType().FullName == "UnityEditor.ShortcutManagement.ShortcutManagerWindow")
                    {
                        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                        var viewField = w.GetType().GetField("m_View", flags);
                        if (viewField != null)
                        {
                            var view = viewField.GetValue(w);
                            if (view != null)
                            {
                                var searchTextField = view.GetType().GetField("m_SearchTextField", flags);
                                if (searchTextField != null)
                                {
                                    var textField = searchTextField.GetValue(view);
                                    if (textField != null)
                                    {
                                        var valueProp = textField.GetType().GetProperty("value", BindingFlags.Public | BindingFlags.Instance);
                                        if (valueProp != null)
                                        {
                                            valueProp.SetValue(textField, "Quick Toolbar");
                                        }
                                    }
                                }
                            }
                        }

                        var viewControllerField = w.GetType().GetField("m_ViewController", flags);
                        if (viewControllerField != null)
                        {
                            var viewController = viewControllerField.GetValue(w);
                            if (viewController != null)
                            {
                                var setSearch = viewController.GetType().GetMethod("SetSearch", flags);
                                setSearch?.Invoke(viewController, new object[] { "Quick Toolbar" });
                            }
                        }

                        w.Repaint();
                        return;
                    }
                }
            };
        }
    }
}
