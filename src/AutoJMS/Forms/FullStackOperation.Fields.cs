using Microsoft.Web.WebView2.WinForms;
using Sunny.UI;
using AutoJMS.FullStack.UI.OperationCenter;
using System.Threading;
using System.Windows.Forms;

namespace AutoJMS
{
    public partial class FullStackOperation
    {
        private string _selectedSource = "LOCAL";
        private string _selectedTimeInterval = "2 PHÚT";
        private string _selectedStatusSelect = "Tất cả tồn kho";
        private string _searchText = "";
        private string _fullStackStatus = "Chưa tải";
        private string _activeJourneyWaybillNo = string.Empty;
        private CancellationTokenSource _journeyLoadCts;
    }
}

