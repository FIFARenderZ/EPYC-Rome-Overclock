namespace RomeOverclock
{
    public class CoreListItem
    {
        public uint CCD { get; }
        public uint CCX { get; }
        public uint CORE { get; }

        public CoreListItem(uint ccd, uint ccx, uint core)
        {
            CCD = ccd;
            CCX = ccx;
            CORE = core;
        }

        public override string ToString()
        {
            return $"Core {CORE + 1}";
        }
    }
}