namespace LockerTests
{
    using System;

    /// <summary>
    /// Represents errors that are related to tests themselves rather than the
    /// features under test.
    /// </summary>
    public class LockerTestException : Exception
    {
        public LockerTestException()
        {
        }

        public LockerTestException(string message)
            : base(message)
        {
        }
    }
}
