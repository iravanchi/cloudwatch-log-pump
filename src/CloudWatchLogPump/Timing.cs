namespace CloudWatchLogPump
{
    public static class Timing
    {
        // All timing constants are here so that relationship between them can be seen and considered easier
        
        public static class Monitor
        {
            // Consider maximum wait times in IterationTargetCall - this should be much larger
            public const int SecondsBeforeConsiderRunningJobUnresponsive = 20 * 60;
            
            public const int SecondsBetweenReCheckingJobRunners = 30;
        }

        public static class Runner
        {
            public const int WaitMillisOnException = 2 * 60 * 1000;
            public const int MinWaitMillisOnIdle = 500;
        }

        public static class IterationTargetCall
        {
            public const int InitialWaitRangeMillis = 2000;
            public const int WaitRangeMultiplier = 4;
            public const int MaxWaitRangeMillis = InitialWaitRangeMillis * WaitRangeMultiplier * WaitRangeMultiplier * WaitRangeMultiplier;
        }
    }
}