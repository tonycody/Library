using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Lair
{
    public enum DownloadState
    {
        Downloading = 0,
        Completed = 1,
        Error = 2,
    }

    sealed class DownloadItem
    {
        public DownloadState State { get; set; }
        public Key Key { get; set; }

        public SectionProfileContent SectionProfileContent { get; set; }
        public SectionMessageContent SectionMessageContent { get; set; }
        public DocumentPageContent DocumentPageContent { get; set; }
        public DocumentOpinionContent DocumentOpinionContent { get; set; }
        public ChatTopicContent ChatTopicContent { get; set; }
        public ChatMessageContent ChatMessageContent { get; set; }
    }
}
