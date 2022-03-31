﻿using System.Collections.Generic;

namespace UrlTracker.Web.Controllers.Models
{
    public class GetRedirectsResponse
    {
        public GetRedirectsResponse(IReadOnlyCollection<RedirectViewModel> entries, int numberOfEntries)
        {
            Entries = entries;
            NumberOfEntries = numberOfEntries;
        }

        public IReadOnlyCollection<RedirectViewModel> Entries { get; set; }
        public int NumberOfEntries { get; set; }
    }
}
