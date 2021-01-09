using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CloudWatchLogPump.Model;

namespace CloudWatchLogPump
{
    public class ProgressDb
    {
        public JobProgress Get(string jobId)
        {
            throw new NotImplementedException();
        }

        public async Task Set(string jobId, JobProgress progress)
        {
            throw new NotImplementedException();
        }

        public void LoadAll(IEnumerable<string> @select)
        {
            throw new System.NotImplementedException();
        }
    }
}