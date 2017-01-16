using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TtnAzureBridge
{
    public class WhiteList
    {
        private List<WhiteListItem> _whiteListItems;

        public int Load(string filename)
        {
            if (!File.Exists(filename))
            {
                return -1;
            }

            var jsonMessage = File.ReadAllText(filename);

            var whiteListItems = JsonConvert.DeserializeObject<WhiteListItem[]>(jsonMessage);

            _whiteListItems = whiteListItems.Where(x => !string.IsNullOrEmpty(x.accept)).ToList();

            return _whiteListItems.Count;
        }

        public bool Accept(string deviceId)
        {
            if (_whiteListItems == null
                || _whiteListItems.Count == 0)
            {
                return true;
            }

            return _whiteListItems.Exists(x => x.accept == deviceId);
        }
    }

    public class WhiteListItem
    {
        public string accept { get; set; }
    }
}