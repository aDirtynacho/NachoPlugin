using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.Components;
using VRage.Scripting;

namespace DedicatedPlugin
{
    public class NachoPlayerSystem : MySessionComponentBase
    {
        public NachoPlugin nachoPlugin;

        public NachoPlayerSystem()
        {
            nachoPlugin = new NachoPlugin();
        }

        public void HandlePowerCommand()
        {

        }
    }
}
