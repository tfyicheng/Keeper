using System;
using System.Collections.Generic;
using Keeper.Infrastructure;

namespace Keeper.Models
{
    /// <summary>
    /// 一次工作状态快照。
    /// </summary>
    public class SnapshotInfo : ObservableObject
    {
        private string _name;
        private int _itemCount;

        public SnapshotInfo()
        {
            Items = new List<SnapshotItem>();
        }

        public long Id { get; set; }

        public string Name
        {
            get { return _name; }
            set { SetProperty(ref _name, value); }
        }

        public DateTime CreatedAt { get; set; }

        public int ItemCount
        {
            get { return _itemCount; }
            set
            {
                if (SetProperty(ref _itemCount, value))
                {
                    OnPropertyChanged(nameof(ItemCountText));
                }
            }
        }

        public IList<SnapshotItem> Items { get; set; }

        public string CreatedAtText
        {
            get { return CreatedAt.ToString("yyyy-MM-dd HH:mm"); }
        }

        public string ItemCountText
        {
            get { return ItemCount + " 个启动项"; }
        }
    }
}
