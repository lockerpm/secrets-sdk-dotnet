namespace Locker
{
    public class EnvironmentRetrieveOptions : BaseOptions
    {
        [Obsolete("Human CLI argument construction is not supported by protocol v1.")]
        public override string BuildOptions()
        {
            string cli = "";

            return cli;
        }
    }
}
