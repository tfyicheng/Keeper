using Keeper.Infrastructure;

namespace Keeper.ViewModels
{
    /// <summary>
    /// 左侧导航项。
    /// </summary>
    public class NavItem : ObservableObject
    {
        private bool _isSelected;

        public string Title { get; set; }

        public string Description { get; set; }

        public string Glyph { get; set; }

        public object Page { get; set; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }
    }
}
