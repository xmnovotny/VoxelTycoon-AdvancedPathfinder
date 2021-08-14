using ModSettingsUtils;
using Newtonsoft.Json;

namespace AdvancedPathfinder
{
    [JsonObject(MemberSerialization.OptOut)]
    internal class Settings : ModSettings<Settings>
    {
        private bool _highlightTrainPaths;
        private bool _highlightAllTrainPaths;
        private bool _highlightReservedPaths;
        private bool _highlightReservedPathsExtended;

        public bool HighlightTrainPaths { 
            get => _highlightTrainPaths;
            set =>  SetProperty<bool>(value, ref _highlightTrainPaths);
        }

        public bool HighlightAllTrainPaths { 
            get => _highlightAllTrainPaths;
            set =>  SetProperty(value, ref _highlightAllTrainPaths);
        }
        
        public bool HighlightReservedPaths
        {
            get => _highlightReservedPaths; 
            set => SetProperty<bool>(value, ref _highlightReservedPaths);
        }

        public bool HighlightReservedPathsExtended
        {
            get => _highlightReservedPathsExtended;
            set => SetProperty<bool>(value, ref _highlightReservedPathsExtended);
        }
    }
}
