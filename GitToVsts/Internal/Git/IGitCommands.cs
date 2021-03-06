namespace GitToVsts.Internal.Git
{
    /// <summary>
    ///     Git Commands Interface
    /// </summary>
    public interface IGitCommands
    {
        /// <summary>
        /// </summary>
        string Clone { get; }

        /// <summary>
        /// </summary>
        string Config { get; }

        /// <summary>
        /// </summary>
        string Reset { get; }

        /// <summary>
        /// </summary>
        string RemoteAdd { get; }

        /// <summary>
        /// </summary>
        string PushAll { get; }

        /// <summary>
        /// </summary>
        string PushTags { get; }
    }
}