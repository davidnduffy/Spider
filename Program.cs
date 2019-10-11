namespace Spider
{
    /// <summary>
    /// Main program entry point class.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main entry point.
        /// </summary>
        /// <param name="args">Command line arguments (not used).</param>
        static void Main(string[] args)
        {
            Downloader down = new Downloader();
            down.Run();
        }
    }
}