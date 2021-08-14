using ModSettingsUtils;
using VoxelTycoon;
using VoxelTycoon.Game.UI;
using VoxelTycoon.Localization;

namespace AdvancedPathfinder.UI
{
    public class SettingsWindowPage : ModSettingsWindowPage
    {
        protected override void InitializeInternal(SettingsControl settingsControl)
        {
            Settings settings = Settings.Current;
            Locale locale = LazyManager<LocaleManager>.Current.Locale;
            settingsControl.AddToggle(locale.GetString("advanced_pathfinder_mod/highlight_train_path"), null, settings.HighlightTrainPaths, delegate ()
            {
                settings.HighlightTrainPaths = true;
            }, delegate ()
            {
                settings.HighlightTrainPaths = false;
            });

            settingsControl.AddToggle(locale.GetString("advanced_pathfinder_mod/highlight_all_train_path"), 
                locale.GetString("advanced_pathfinder_mod/highlight_all_train_path_notice"), settings.HighlightAllTrainPaths, delegate ()
            {
                settings.HighlightAllTrainPaths = true;
            }, delegate ()
            {
                settings.HighlightAllTrainPaths = false;
            });

            settingsControl.AddToggle(locale.GetString("advanced_pathfinder_mod/highlight_reserved_path"), null, settings.HighlightReservedPaths, delegate ()
            {
                settings.HighlightReservedPaths = true;
            }, delegate ()
            {
                settings.HighlightReservedPaths = false;
            });

            settingsControl.AddToggle(locale.GetString("advanced_pathfinder_mod/highlight_reserved_path_extended"), null, settings.HighlightReservedPathsExtended, delegate ()
            {
                settings.HighlightReservedPathsExtended = true;
            }, delegate ()
            {
                settings.HighlightReservedPathsExtended = false;
            });

        }

    }
}
