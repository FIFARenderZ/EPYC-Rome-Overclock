namespace RomeOverclock
{
    public class FrequencyListItem
    {
        public int Frequency { get; }
        public string Display { get; }

        public FrequencyListItem(int frequency, string display)
        {
            this.Frequency = frequency;
            this.Display = display;
        }

        public override string ToString()
        {
            return Display;
        }
    }
}