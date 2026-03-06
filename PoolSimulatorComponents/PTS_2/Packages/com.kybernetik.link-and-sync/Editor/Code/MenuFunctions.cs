// Link & Sync // Copyright 2022 Kybernetik //

#if UNITY_EDITOR

using System.Collections.Generic;
using UnityEditor;

namespace LinkAndSync
{
    public static class MenuFunctions
    {
        /************************************************************************************************************************/

        public const string
            MenuPrefix = "Assets/Link and Sync/",
            SyncAllFunction = MenuPrefix + "Sync All Links",
            SyncSelectedFunction = MenuPrefix + "Sync Selected Links",
            TwoWaySyncFunction = MenuPrefix + "Two Way Sync Selected Links",
            ExcludeSelectionFunction = MenuPrefix + "Exclude Selection from Link";

        public const int
            MenuPriority = 100,
            SyncAllPriority = MenuPriority + 1,
            SyncSelectedPriority = MenuPriority + 2,
            TwoWaySyncPriority = MenuPriority + 3,
            ExcludeSelectionPriority = MenuPriority + 4;

        /************************************************************************************************************************/

        [MenuItem(SyncSelectedFunction, priority = SyncSelectedPriority)]
        private static void SyncSelected()
        {
            AssetDatabase.SaveAssets();

            foreach (var link in LasUtilities.GatherSelectedLinks())
                SyncOperationWindow.Show(new SyncOperation(link), false);
        }

        [MenuItem(SyncSelectedFunction, priority = SyncSelectedPriority, validate = true)]
        [MenuItem(TwoWaySyncFunction, priority = TwoWaySyncPriority, validate = true)]
        private static bool ValidateSyncSelected()
        {
            foreach (var _ in LasUtilities.GatherSelectedLinks())
                return true;

            return false;
        }

        /************************************************************************************************************************/

        [MenuItem(SyncAllFunction, priority = SyncAllPriority)]
        private static void SyncAll()
        {
            AssetDatabase.SaveAssets();

            foreach (var link in LinkAndSync.GetAllLinks())
                SyncOperationWindow.Show(new SyncOperation(link), false);
        }

        [MenuItem(SyncAllFunction, priority = SyncAllPriority, validate = true)]
        private static bool ValidateSyncAll()
        {
            foreach (var _ in LinkAndSync.GetAllLinks())
                return true;

            return false;
        }

        /************************************************************************************************************************/

        [MenuItem(ExcludeSelectionFunction, priority = ExcludeSelectionPriority)]
        private static void ExcludeFromLink()
        {
            var links = new List<LinkAndSync>();

            foreach (var selected in Selection.objects)
            {
                links.Clear();
                LinkAndSync.GetContainingLinks(selected, out var assetPath, links);

                foreach (var link in links)
                {
                    if (selected == link)
                        continue;

                    var linkRoot = link.DirectoryPath;
                    var relativePath = assetPath.Substring(linkRoot.Length + 1);
                    if (!link.Exclusions.Contains(relativePath))
                        link.Exclusions.Add(relativePath);
                }
            }

            ProjectWindowOverlay.ClearCache();
        }

        [MenuItem(ExcludeSelectionFunction, priority = ExcludeSelectionPriority, validate = true)]
        private static bool ValidateExcludeFromLink()
        {
            var links = new List<LinkAndSync>();

            foreach (var selected in Selection.objects)
            {
                LinkAndSync.GetContainingLinks(selected, out _, links);

                foreach (var link in links)
                {
                    if (selected == link)
                        continue;

                    return true;
                }
            }

            return false;
        }

        /************************************************************************************************************************/

        [MenuItem(TwoWaySyncFunction, priority = TwoWaySyncPriority)]
        private static void TwoWaySync()
        {
            AssetDatabase.SaveAssets();

            foreach (var link in LasUtilities.GatherSelectedLinks())
                SyncOperationWindow.Show(new SyncOperation(link, SyncDirection.Sync), false);
        }

        /************************************************************************************************************************/
    }
}

#endif