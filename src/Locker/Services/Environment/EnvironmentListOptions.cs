namespace Locker
{
    public class EnvironmentListOptions : ListOptions
    {
        [Obsolete("Human CLI argument construction is not supported by protocol v1.")]
        public override string BuildOptions()
        {
            string cli = "";

            return cli;
        }
    }
}
