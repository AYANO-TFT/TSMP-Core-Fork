namespace K13A.TSMP
{
    public static class TransSyncSendScheduler
    {
        public const int ScratchBytes = NetworkFrameProtocol.UInt16MaxValue + NetworkFrameProtocol.VariableValueHeaderBytes;

        public static float NormalizeInterval(float interval)
        {
            return interval > 0f && !float.IsInfinity(interval) ? interval : 0f;
        }

        public static int GetPriority(int[] priorities, int index)
        {
            return priorities != null && index < priorities.Length ? priorities[index] : 0;
        }

        public static bool GetSendOnChange(bool[] options, int index)
        {
            return options == null || index >= options.Length || options[index];
        }

        public static float GetInterval(float[] intervals, int index)
        {
            return intervals != null && index < intervals.Length ? NormalizeInterval(intervals[index]) : 0f;
        }

        public static int[] BuildOrder(int count, int[] priorities)
        {
            int[] order = new int[count];
            for (int i = 0; i < count; i++)
            {
                int position = i;
                int priority = GetPriority(priorities, i);
                while (position > 0 && GetPriority(priorities, order[position - 1]) < priority)
                {
                    order[position] = order[position - 1];
                    position--;
                }
                order[position] = i;
            }
            return order;
        }

        public static bool IsDue(bool sent, double lastSent, double now, float interval)
        {
            return !sent || now < lastSent || now - lastSent >= NormalizeInterval(interval);
        }

        public static bool ShouldSend(bool sent, double lastSent, double now, bool sendOnChange, float refreshInterval, byte[] previous, byte[] candidate, int length)
        {
            if (!sent || !sendOnChange || now < lastSent)
                return true;
            float refresh = NormalizeInterval(refreshInterval);
            if (refresh > 0f && now - lastSent >= refresh)
                return true;
            if (previous == null || previous.Length != length)
                return true;
            for (int i = 0; i < length; i++)
            {
                if (previous[i] != candidate[i])
                    return true;
            }
            return false;
        }

        public static byte[] Snapshot(byte[] source, int offset, int length, byte[] previous)
        {
            byte[] result = previous;
            if (result == null || result.Length != length)
                result = new byte[length];
            System.Array.Copy(source, offset, result, 0, length);
            return result;
        }
    }
}
