using Colossal.Logging;
using Game;
using Game.Modding;

namespace SteppedStreets
{
    public sealed class Mod : IMod
    {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(SteppedStreets)).SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            Logger.Info("Stepped Streets loading");
            try
            {
                updateSystem.UpdateAt<Build.TerrainBuildSystem>(SystemUpdatePhase.ToolUpdate);
            }
            catch (System.Exception exception)
            {
                Logger.Error(exception, "Stepped Streets terrain registration failed.");
            }
        }

        public void OnDispose()
        {
        }
    }
}
