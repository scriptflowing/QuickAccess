using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Universal.QuickAccess
{
    public class MenuTreeNode
    {
        public string Name;
        public string FullPath;
        public List<MenuTreeNode> Children = new List<MenuTreeNode>();
        public bool Expanded;
        public bool IsLeaf => FullPath != null;
    }

    public class QuickAccessSetting : EditorWindow
    {
        private Action<string, bool> _onToggle;
        private string _searchText = "";
        private Vector2 _scrollPos;
        private MenuTreeNode _root;
        private HashSet<string> _existingPaths;
        private bool _focusSearch;
        private static bool _showSelectedOnly;
        private static bool _expandAllToggle;

        private QuickAccessConfigData _config;
        private HashSet<string> _snapshotPaths;

        private static MenuTreeNode _cachedRoot;
        private static QuickAccessSetting _openInstance;

        public static bool IsOpen => _openInstance != null;

        // Static constructor clears cache on domain reload
        static QuickAccessSetting()
        {
            _cachedRoot = null;
            _openInstance = null;
        }

        public static void OnOpen()
        {
            if (_openInstance != null)
            {
                _openInstance._config = QuickAccessConfig.Instance;
                _openInstance._existingPaths = GetExistingPaths(_openInstance._config);
                if (_showSelectedOnly)
                    _openInstance._snapshotPaths = new HashSet<string>(_openInstance._existingPaths);
                else
                    _openInstance._snapshotPaths = null;
                _openInstance._onToggle = _openInstance.HandleConfigToggle;
                _openInstance._searchText = "";
                _openInstance._focusSearch = true;
                _openInstance.Focus();
                return;
            }

            EditorApplication.delayCall += OpenWindow;
        }

        private static HashSet<string> GetExistingPaths(QuickAccessConfigData config)
        {
            var paths = new HashSet<string>();
            if (config?.items != null)
            {
                foreach (var item in config.items)
                {
                    if (!string.IsNullOrEmpty(item.menuPath))
                        paths.Add(item.menuPath);
                }
            }
            return paths;
        }

        private void HandleConfigToggle(string path, bool added)
        {
            if (added)
            {
                var existingItem = _config.items.Find(i => i.menuPath == path);
                if (existingItem == null)
                {
                    var name = path.Split('/').Last();
                    if (name.EndsWith("...")) name = name.Substring(0, name.Length - 3);

                    _config.items.Add(new QuickAccessMenuItem
                    {
                        displayName = name,
                        menuPath = path,
                        groupName = GuessGroup(path)
                    });
                    _existingPaths.Add(path);
                    QuickAccessConfig.Save(_config);
                }
            }
            else
            {
                var item = _config.items.Find(i => i.menuPath == path);
                if (item != null)
                {
                    _config.items.Remove(item);
                    _existingPaths.Remove(path);
                    QuickAccessConfig.Save(_config);
                }
            }
        }

        private static void OpenWindow()
        {
            var window = CreateInstance<QuickAccessSetting>();

            window._config = QuickAccessConfig.Instance;
            window._existingPaths = GetExistingPaths(window._config);
            if (_showSelectedOnly)
                window._snapshotPaths = new HashSet<string>(window._existingPaths);
            window._onToggle = window.HandleConfigToggle;
            window.titleContent = new GUIContent("Quick Access Settings");

            window.minSize = new Vector2(300, 350);
            window.position = new Rect(100, 100, 400, 500);
            window.ShowUtility();
            _openInstance = window;
            window._focusSearch = true;
            if (_cachedRoot != null)
            {
                window._root = _cachedRoot;
            }
            else
            {
                window.BuildMenuTree();
            }
        }

        private void BuildMenuTree()
        {
            var menuPaths = new HashSet<string>();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch { continue; }

                foreach (var type in types)
                {
                    MethodInfo[] methods;
                    try { methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
                    catch { continue; }
                    if (methods == null) continue;

                    foreach (var method in methods)
                    {
                        object[] attrs;
                        try { attrs = method.GetCustomAttributes(false); }
                        catch { continue; }
                        if (attrs == null) continue;
                        foreach (var attr in attrs)
                        {
                            if (attr.GetType().Name != "MenuItem") continue;
                            var field = attr.GetType().GetField("menuItem", BindingFlags.Public | BindingFlags.Instance);
                            if (field == null) continue;

                            var path = field.GetValue(attr) as string;
                            if (string.IsNullOrEmpty(path)) continue;
                            if (path.StartsWith("CONTEXT/") || path.StartsWith("internal:")) continue;

                            menuPaths.Add(path);
                        }
                    }
                }
            }

            _root = new MenuTreeNode { Name = "Root" };
            _root.Expanded = true;
            foreach (var path in menuPaths.OrderBy(p => p))
            {
                InsertPath(_root, path);
            }
            _cachedRoot = _root;
        }

        private static void InsertPath(MenuTreeNode root, string path)
        {
            var parts = path.Split('/');
            var current = root;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var child = current.Children.Find(c => c.Name == part);

                if (child == null)
                {
                    child = new MenuTreeNode
                    {
                        Name = part,
                        FullPath = (i == parts.Length - 1) ? path : null
                    };
                    current.Children.Add(child);
                }
                else if (i == parts.Length - 1)
                {
                    child.FullPath = path;
                }

                current = child;
            }
        }

        private static readonly string[] AnchorDirectionOptions = { "TopLeft", "TopRight", "BottomLeft", "BottomRight", "Center" };
        private int _anchorDirectionIndex;


        private void OnGUI()
        {
            EditorGUILayout.Space();

            GUI.SetNextControlName("Search");
            _searchText = EditorGUILayout.TextField(_searchText, EditorStyles.toolbarSearchField);

            if (_focusSearch)
            {
                GUI.FocusControl("Search");
                _focusSearch = false;
            }

            EditorGUILayout.Space();

            // Config mode controls — hidden when searching
            if (string.IsNullOrEmpty(_searchText))
            {
                {
                    GUILayout.BeginHorizontal();

                    _anchorDirectionIndex = Array.IndexOf(AnchorDirectionOptions, _config.anchorDirection ?? "TopLeft");
                    if (_anchorDirectionIndex < 0) _anchorDirectionIndex = 0;

                    EditorGUI.BeginChangeCheck();
                    _config.followMouse = EditorGUILayout.ToggleLeft("Follow Mouse", _config.followMouse, GUILayout.Width(110));
                    if (EditorGUI.EndChangeCheck())
                    {
                        QuickAccessConfig.Save(_config);
                    }

                    EditorGUI.BeginDisabledGroup(!_config.followMouse);
                    EditorGUI.BeginChangeCheck();
                    _anchorDirectionIndex = EditorGUILayout.Popup(_anchorDirectionIndex, AnchorDirectionOptions, GUILayout.Width(100));
                    if (EditorGUI.EndChangeCheck())
                    {
                        _config.anchorDirection = AnchorDirectionOptions[_anchorDirectionIndex];
                        QuickAccessConfig.Save(_config);
                    }
                    EditorGUI.EndDisabledGroup();

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Shortcuts", EditorStyles.miniButton, GUILayout.Width(70), GUILayout.Height(18)))
                    {
                        OpenShortcutManager();
                    }

                    GUILayout.EndHorizontal();
                }

                {
                    GUILayout.BeginHorizontal();

                    EditorGUI.BeginChangeCheck();
                    _expandAllToggle = EditorGUILayout.ToggleLeft("Expand All", _expandAllToggle, GUILayout.Width(100));
                    if (EditorGUI.EndChangeCheck())
                    {
                        SetAllExpanded(_root, _expandAllToggle);
                        FitToContent();
                    }

                    EditorGUI.BeginChangeCheck();
                    _showSelectedOnly = EditorGUILayout.ToggleLeft("Filter Selected", _showSelectedOnly, GUILayout.Width(110));
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (_showSelectedOnly)
                        {
                            _snapshotPaths = new HashSet<string>(_existingPaths);
                            SetAllExpanded(_root, true);
                        }
                        else
                        {
                            SetAllExpanded(_root, _expandAllToggle);
                        }
                        FitToContent();
                    }

                    GUILayout.EndHorizontal();
                }

                EditorGUILayout.Space();
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            if (!string.IsNullOrEmpty(_searchText))
            {
                var results = new List<string>();
                CollectSearchResults(_root, _searchText, results);

                if (_showSelectedOnly)
                {
                    results = results.Where(p => IsVisibleInSelectedOnlyMode(p)).ToList();
                }

                if (results.Count == 0)
                {
                    EditorGUILayout.LabelField("No matching menus.", EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    foreach (var path in results)
                    {
                        var isExisting = _existingPaths != null && _existingPaths.Contains(path);
                        var displayName = path.Replace("/", " \u25B8 ");

                        var newValue = EditorGUILayout.ToggleLeft(displayName, isExisting);
                        if (newValue != isExisting)
                        {
                            _onToggle?.Invoke(path, newValue);
                        }
                    }
                }
            }
            else
            {
                foreach (var child in _root.Children.OrderBy(c => c.Name))
                {
                    DrawNode(child, 0);
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
        }

        private void DrawNode(MenuTreeNode node, int depth)
        {
            var indent = depth * 16f;

            if (node.IsLeaf)
            {
                if (_showSelectedOnly && !IsVisibleInSelectedOnlyMode(node.FullPath))
                    return;

                var isExisting = _existingPaths != null && _existingPaths.Contains(node.FullPath);

                GUILayout.BeginHorizontal();
                GUILayout.Space(indent + 16);

                var newValue = EditorGUILayout.ToggleLeft(node.Name, isExisting, GUILayout.ExpandWidth(true));
                if (newValue != isExisting)
                {
                    _onToggle?.Invoke(node.FullPath, newValue);
                }

                GUILayout.EndHorizontal();
            }
            else
            {
                if (_showSelectedOnly && !BranchHasSelected(node))
                    return;

                GUILayout.BeginHorizontal();
                GUILayout.Space(indent);

                var foldoutContent = new GUIContent(" " + node.Name, EditorGUIUtility.IconContent("Folder Icon").image);
                var expanded = EditorGUILayout.Foldout(node.Expanded, foldoutContent, true);
                if (expanded != node.Expanded)
                {
                    node.Expanded = expanded;
                    FitToContent();
                }

                GUILayout.EndHorizontal();

                if (node.Expanded)
                {
                    foreach (var child in node.Children.OrderBy(c => c.IsLeaf ? 1 : 0).ThenBy(c => c.Name))
                    {
                        DrawNode(child, depth + 1);
                    }
                }
            }
        }

        private bool BranchHasSelected(MenuTreeNode node)
        {
            foreach (var child in node.Children)
            {
                if (child.IsLeaf)
                {
                    if (IsVisibleInSelectedOnlyMode(child.FullPath))
                        return true;
                }
                else
                {
                    if (BranchHasSelected(child))
                        return true;
                }
            }
            return false;
        }

        private static bool AreAllExpanded(MenuTreeNode node)
        {
            if (node.IsLeaf) return true;
            if (!node.Expanded) return false;
            return node.Children.All(c => AreAllExpanded(c));
        }

        private static void SetAllExpanded(MenuTreeNode node, bool expanded)
        {
            if (!node.IsLeaf)
            {
                node.Expanded = expanded;
                node.Expanded |= node.Name == "Root";
                foreach (var child in node.Children)
                    SetAllExpanded(child, expanded);
            }
        }

        private void FitToContent()
        {
            var contentHeight = CountVisibleNodes(_root) * 18f + 120f;
            var newHeight = Mathf.Clamp(contentHeight, minSize.y, 800);
            position = new Rect(position.x, position.y, position.width, newHeight);
        }

        private int CountVisibleNodes(MenuTreeNode node)
        {
            if (node.IsLeaf)
            {
                if (_showSelectedOnly && !IsVisibleInSelectedOnlyMode(node.FullPath))
                    return 0;
                return 1;
            }

            if (_showSelectedOnly && !BranchHasSelected(node))
                return 0;

            if (!node.Expanded) return 1;
            var count = 1;
            foreach (var child in node.Children)
                count += CountVisibleNodes(child);
            return count;
        }

        private bool IsVisibleInSelectedOnlyMode(string path)
        {
            return _snapshotPaths != null && _snapshotPaths.Contains(path);
        }

        private void CollectSearchResults(MenuTreeNode node, string search, List<string> results)
        {
            if (node.IsLeaf)
            {
                if (node.FullPath.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    results.Add(node.FullPath);
                }
            }
            else
            {
                foreach (var child in node.Children)
                    CollectSearchResults(child, search, results);
            }
        }

        private void OnLostFocus()
        {
            Close();
        }

        private void OnDisable()
        {
            _openInstance = null;

            EditorApplication.delayCall += () =>
            {
                var hub = Resources.FindObjectsOfTypeAll<QuickAccessHub>();
                if (hub.Length > 0)
                    hub[0].Focus();
            };
        }

        private void OnDestroy()
        {
        }

        private static string GuessGroup(string menuPath)
        {
            var slashIndex = menuPath.IndexOf('/');
            return slashIndex > 0 ? menuPath.Substring(0, slashIndex) : "";
        }

        private static void OpenShortcutManager()
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
                                            valueProp.SetValue(textField, "Quick Access");
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
                                setSearch?.Invoke(viewController, new object[] { "Quick Access" });
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
