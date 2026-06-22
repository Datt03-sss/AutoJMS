namespace AutoJMS
{
    public partial class FullStackOperation
    {
        private void BindCodeFirstEvents()
        {
            Load += FullStackOperation_Load;
            FormClosing += FullStackOperation_FormClosing;
        }

        private void WireCodeFirstEvents()
        {
            BindCodeFirstEvents();
        }
    }
}

