using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal static class ModelessReviewController
    {
        private static JoinResultReviewForm _form;
        private static RevitNavigationRequestHandler _handler;
        private static ExternalEvent _externalEvent;

        public static void ShowOrUpdate(string title, string summary, System.Collections.Generic.IList<JoinFailureDetail> failures)
        {
            if (_handler == null)
            {
                _handler = new RevitNavigationRequestHandler();
                _externalEvent = ExternalEvent.Create(_handler);
            }

            if (_form == null || _form.IsDisposed)
            {
                _form = new JoinResultReviewForm(_handler, _externalEvent);
            }

            _form.LoadResult(title, summary, failures);
            if (!_form.Visible)
            {
                _form.Show();
            }

            _form.BringToFront();
            _form.Focus();
        }
    }
}
