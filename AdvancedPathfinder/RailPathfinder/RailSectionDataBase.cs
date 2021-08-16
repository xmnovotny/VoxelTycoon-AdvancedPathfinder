namespace AdvancedPathfinder.RailPathfinder
{
    public class RailSectionDataBase
    {
        private SectionDirection _allowedDirection = SectionDirection.Both;
        private bool _hasPlatform = false;
        private bool _isElectrified = true;

        public bool IsElectrified
        {
            get => _isElectrified;
            set => _isElectrified &= value;
        }

        public SectionDirection AllowedDirection
        {
            get => _allowedDirection;
            set => _allowedDirection = _allowedDirection.Combine(value, false);
        }

        public bool HasPlatform
        {
            get => _hasPlatform;
            set => _hasPlatform |= value;
        }

        public float Length { get; private set; }
        public float LengthAdd { set => Length += value; }
        public float CurvedLength { get; private set; }
        public float CurvedLengthAdd { set => CurvedLength += value; }

        public bool IsValidDirection(PathDirection direction)
        {
            return AllowedDirection.CanPass(direction);
        }

        public virtual void Combine(RailSectionDataBase data, bool isReversed)
        {
            IsElectrified = data.IsElectrified;
            HasPlatform = data.HasPlatform;
            Length += data.Length;
            CurvedLength += data.CurvedLength;
            CombineAllowedDirection(data.AllowedDirection, isReversed);
        }

        public virtual void Reset()
        {
            _hasPlatform = false;
            _isElectrified = true;
            _allowedDirection = SectionDirection.Both;
            Length = 0;
            CurvedLength = 0;
        }

        public void CombineAllowedDirection(SectionDirection direction, bool isReversed)
        {
            _allowedDirection = _allowedDirection.Combine(direction, isReversed);
        }
    }
}