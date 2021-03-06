﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bivrost.Bivrost360Player
{
	public class HeadsetMenuViewModel : FadeMenuBase
	{
		public event Action OnRift = delegate { };
		public event Action OnOSVR = delegate { };
		public event Action OnDisable = delegate { };
		public event Action OnVive = delegate { };

		public void SelectRift()
		{
			this.Hide(0.25f);
			OnRift();
		}

		public void SelectOSVR()
		{
			this.Hide(0.25f);
			OnOSVR();
		}

        public void SelectOff()
        {
			this.Hide(0.25f);
			OnDisable();
		}

        public void SelectVive()
        {
			this.Hide(0.25f);
			OnVive();
		}
	}
}
