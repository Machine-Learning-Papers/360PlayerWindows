﻿using Caliburn.Micro;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlayerUI
{
    public partial class ShellViewModel
    {
        private static bool IsRemoteControlEnabled = false;

        private void EnableRemoteControl()
        {
            Task.Factory.StartNew(() =>
            {
                remoteControl = ApiServer.InitNancy(apiServer =>
                    {
                        Log("got unity init, device_id=" + ApiServer.device_id + " movies=[\n" + string.Join(",\n", ApiServer.movies) + "]", ConsoleColor.DarkGreen);
                        Log("init complete", ConsoleColor.DarkGreen);
                    }
                );

                #region GearVR slave app

                ApiServer.OnBackPressed += () =>
                {
                    Log("[remote] back pressed", ConsoleColor.DarkGreen);
                };

                ApiServer.OnStateChange += (state) => {
                    Log("[remote] state changed: " + state, ConsoleColor.DarkGreen);

                    switch (state)
                    {
                        //case ApiServer.State.off:
                        //	Execute.OnUIThreadAsync(() =>
                        //	{
                        //		if(IsPlaying)
                        //			Stop();
                        //	});
                        //	break;

                        case ApiServer.State.pause:
                            Execute.OnUIThreadAsync(() =>
                            {
                                if (IsPlaying && !IsPaused)
                                {
                                    Pause();
                                    _mediaDecoder.Seek(remoteTime);
                                }
                            });
                            break;

                        case ApiServer.State.play:
                            Execute.OnUIThreadAsync(() =>
                            {
                                if (!_ready)
                                    OpenFileFrom(SelectedFileName);
                                else
                                    PlayPause();
                            });
                            break;

                        case ApiServer.State.stop:
                            Execute.OnUIThreadAsync(() =>
                            {
                                if (IsPlaying)
                                    Stop();
                            });
                            break;
                    }
                };

                ApiServer.OnConfirmPlay += (path) =>
                {
                    Log("[remote] path = " + path, ConsoleColor.Green);
                    string remoteFile = path.Split('/').Last();
                    if (File.Exists(Logic.Instance.settings.RemoteControlMovieDirectory + Path.DirectorySeparatorChar + remoteFile))
                    {
                        IsFileSelected = true;
                        SelectedFileName = Logic.Instance.settings.RemoteControlMovieDirectory + Path.DirectorySeparatorChar + remoteFile;
                    }
                    else
                    {
                        IsFileSelected = false;
                        SelectedFileName = "";

                        Execute.OnUIThreadAsync(() => NotificationCenter.PushNotification(new NotificationViewModel("Requested file \"" + remoteFile + "\" not found in video library.",
                            () => {
                                System.Diagnostics.Process.Start(Logic.Instance.settings.RemoteControlMovieDirectory);
                            }, "open folder")));
                    }
                };

                ApiServer.OnPos += (euler, t) =>
                {
                    //Log("[remote] position = " + euler.Item1 + ", " + euler.Item2 + ", " + euler.Item3, ConsoleColor.DarkGreen);
                    //remoteTime = (float)(t * MaxTime);
                    //if (DXCanvas.Scene != null)
                    //{
                    //	((Scene)DXCanvas.Scene).SetLook(euler);
                    //}
                };

                ApiServer.OnPosQuaternion += (quat, t) =>
                {
                    //Log("[remote] position = " + quat.Item1 + ", " + euler.Item2 + ", " + euler.Item3, ConsoleColor.DarkGreen);
                    remoteTime = (float)(t * MaxTime);
                    if (DXCanvas.Scene != null)
                    {
                        ((Scene)DXCanvas.Scene).SetLook(quat);
                    }
                };



                ApiServer.OnInfo += (msg) => {
                    Log("[remote] msg = " + msg, ConsoleColor.DarkGreen);
                };

                #endregion


                #region Remote controll app - control API

                ApiServer.CommandLoadHandler = (movie, autoplay) =>
                {
                    Log("[remote] path = " + movie, ConsoleColor.Green);
                    string remoteFile = movie.Contains('/') ? movie.Split('/').Last() : movie;
                    if (File.Exists(Logic.Instance.settings.RemoteControlMovieDirectory + Path.DirectorySeparatorChar + remoteFile))
                    {
                        IsFileSelected = true;
                        SelectedFileName = Logic.Instance.settings.RemoteControlMovieDirectory + Path.DirectorySeparatorChar + remoteFile;

                        this.LoadMedia(autoplay);

                        return true;
                    }
                    else
                    {
                        IsFileSelected = false;
                        SelectedFileName = "";

                        Execute.OnUIThreadAsync(() => NotificationCenter.PushNotification(new NotificationViewModel("Requested file \"" + remoteFile + "\" not found in video library.",
                            () => {
                                System.Diagnostics.Process.Start(Logic.Instance.settings.RemoteControlMovieDirectory);
                            }, "open folder")));
                        return false;
                    }                    
                };

                ApiServer.CommandMoviesHandler = () =>
                {
                    List<string> movies = new List<string>();
                    var dir = Logic.Instance.settings.RemoteControlMovieDirectory;
                    if (Directory.Exists(dir))
                    {
                        try
                        {
                            var files = Directory.GetFiles(dir);
                            foreach(string file in files)
                            {
                                if (MediaDecoder.CheckExtension(Path.GetExtension(file)))
                                    movies.Add(Path.GetFileName(file));
                            }
                        } catch (Exception exc)
                        {

                        }
                    }
                    return movies.ToArray();
                };

                ApiServer.CommandPauseHandler = () =>
                {
                    if (IsPlaying && !IsPaused)
                    {
                        Pause();
                        return true;
                    }
                    return false;
                };

                ApiServer.CommandPlayingHandler = () =>
                {
                    var info = new ApiServer.PlayingInfo()
                    {
                        is_playing = false,
                        movie = "",
                        quat_x = 0f,
                        quat_y = 0f,
                        quat_z = 0f,
                        quat_w = 0f,
                        t = 0,
                        tmax = 0
                    };

                    if (IsFileSelected) info.movie = SelectedFileName;
                    if (IsPlaying)
                    {
                        info.is_playing = !IsPaused;
                        if (this.DXCanvas.Scene != null)
                        {
                            var q = ((Scene)this.DXCanvas.Scene).GetCurrentLook();
                            info.quat_x = q.X;
                            info.quat_y = q.Y;
                            info.quat_z = q.Z;
                            info.quat_w = q.W;
                        }
                        info.t = (float)TimeValue;
                        info.tmax = (float)MaxTime;
                    }                   

                    return info;
                };

                ApiServer.CommandSeekHandler = (time) =>
                {
                    if (IsPlaying)
                    {
                        TimeValue = time;
                        return true;
                    }
                    return false;
                };

                ApiServer.CommandStopAndResetHandler = () =>
                {
                    if(IsPlaying)
                    {
                        Stop();
                        return true;
                    }
                    return false;
                };

                ApiServer.CommandUnpauseHandler = () =>
                {
                    if (IsPlaying && IsPaused)
                    {
                        UnPause();
                        return true;
                    }
                    if(!IsPlaying && IsFileSelected)
                    {
                        PlayPause();
                        return true;
                    }
                    return false;
                };


                #endregion

                IsRemoteControlEnabled = true;

            });

        }

        public static void SendEvent(string name, object eventParameter = null)
        {
            if(IsRemoteControlEnabled)
            {
                string arg = JsonConvert.SerializeObject(eventParameter);
                ApiServer.AddOutgoingEvent(name, arg);
            }
        }

        //Player events:
        //- movie loaded
        //- movie playback started
        //- movie paused
        //- movie stopped
        //- movie ended
        //- movie seek
        //- playback error
        //- volume changed
        //- full screen state changed
        //- headset connected
        //- headset error
        //- zoom changed
        //- projection changed (gnomic / stereographic)
        //- notification pushed
        //- quit


        }
}