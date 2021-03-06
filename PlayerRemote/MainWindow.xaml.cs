﻿using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using RestSharp;
using System.Collections.Generic;
using System.Threading;

namespace PlayerRemote
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private ControlAPI api;

		TextBlock AddLabel(string text)
		{
			TextBlock tb = new TextBlock()
			{
				Text = text,
				Margin = new Thickness(4),
				TextWrapping = TextWrapping.WrapWithOverflow
			};
			Panel.Children.Add(tb);
			return tb;
		}


		TextBlock AddTitle(string title)
		{
			var tb = AddLabel(title);
			tb.FontWeight = FontWeight.FromOpenTypeWeight(500);
			return tb;
		}

		Button AddButton<T>(string label, Func<Task<T>> handler)
		{
			Button btn = new Button() { Margin = new Thickness(4) };
			btn.Content = label;
			btn.Click += async (s, e) =>
			{
				try
				{
					await handler();
				}
				catch (TaskCanceledException ex)
				{
					Console.WriteLine("task was canceled");
				}
				catch(Exception ex) 
				{
					Console.WriteLine(ex.ToString());
				}
			};
			Panel.Children.Add(btn);
			return btn;
		}

		TextBox AddInput(string label, string defValue = null)
		{
			var tb = new TextBox()
			{
				Margin = new Thickness(4),
				Text = defValue,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Stretch,
				TextWrapping = TextWrapping.WrapWithOverflow
			};
			Grid.SetColumn(tb, 1);
			Grid.SetRow(tb, 0);

			var lb = new Label()
			{
				Target = tb,
				Content = new TextBlock() { Text = label, Width = 100, TextWrapping = TextWrapping.WrapWithOverflow }
			};
			Grid.SetColumn(lb, 0);
			Grid.SetRow(lb, 0);

			var grid = new Grid() { Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Stretch };
			grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(100) });
			grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
			grid.RowDefinitions.Add(new RowDefinition());
			grid.Children.Add(lb);
			grid.Children.Add(tb);

			Panel.Children.Add(grid);

			return tb;
		}

		CheckBox AddCheckbox(string label, bool defValue = false)
		{
			var cb = new CheckBox() { IsChecked = defValue };
			var sp = new StackPanel()
			{
				Orientation = Orientation.Horizontal,
				Margin = new Thickness(4),
				VerticalAlignment = VerticalAlignment.Bottom
			};
			sp.Children.Add(cb);
			sp.Children.Add(new Label() { Target = cb, Content = new TextBlock() { Text = label, TextWrapping = TextWrapping.WrapWithOverflow } });
			Panel.Children.Add(sp);
			return cb;
		}


		Separator AddSeparator()
		{
			Separator sep = new Separator();
			sep.Margin = new Thickness(0, 8, 0, 8);
			Panel.Children.Add(sep);
			return sep;
		}


		public MainWindow()
		{
			InitializeComponent();

			api = new ControlAPI("http://127.0.0.1:8080/v1/", APIDebugger);


			var url = AddInput("API URL", "http://127.0.0.1:8080/v1/");
			Button btn = new Button();
			btn.Content = "change URL";
			btn.Click += (s, e) =>
				api = new ControlAPI(url.Text, APIDebugger);
			Panel.Children.Add(btn);
			AddSeparator();

			AddTitle("GET /v1/");
			AddLabel("Returns the version of the API");
			AddButton("run", async () => await api.Version());
			AddSeparator();

			AddTitle("POST /v1/info");
			var msg = AddInput("message (POST)");
			AddButton("run", async () => await api.Info(msg.Text));
			AddSeparator();

			AddTitle("GET /v1/movies");
            AddButton("run", async () => await api.Movies());
            AddSeparator();

			AddTitle("GET /v1/load");
			var movie = AddInput("movie (GET)");
			var autoplay = AddCheckbox("autoplay (GET)", true);
			AddButton("run", async () => await api.Load(movie.Text, autoplay.IsChecked.GetValueOrDefault()));
			AddSeparator();

			AddTitle("GET /v1/seek");
			var t = AddInput("t (GET)");
			AddButton("run", async () => await api.Seek(float.Parse(t.Text)));
			AddSeparator();

			AddTitle("GET /v1/stop-and-reset");
			AddButton("run", async () => await api.StopAndReset());
			AddSeparator();

			AddTitle("GET /v1/pause");
			AddButton("run", async () => await api.Pause());
			AddSeparator();

			AddTitle("GET /v1/unpause");
			AddButton("run", async () => await api.Unpause());
			AddSeparator();

			AddTitle("GET /v1/playing");
			AddButton("run", async () => await api.Playing());
			AddSeparator();

			AddTitle("GET /v1/events");
			cts = null;
			Button btncancel = null;
			Button btnrunonce = null;
			Button btnrununtilcanceled = null;
			btncancel=AddButton("cancel", async () => { 
				if(cts != null)
					cts.Cancel();
				cts = null;
				btncancel.IsEnabled = false;
				btnrunonce.IsEnabled = btnrununtilcanceled.IsEnabled = true;
				return true;  
			});
			btncancel.IsEnabled = false;

			btnrunonce = AddButton("run once", async () => {
				if (cts != null)
					cts.Cancel(false);
				cts = new CancellationTokenSource();
				btncancel.IsEnabled = true;
				btnrunonce.IsEnabled = btnrununtilcanceled.IsEnabled = false;
				try {
					var events = await api.GetEventsOnce(cts.Token);
					return events;
				} finally {
					btncancel.IsEnabled = false;
					btnrunonce.IsEnabled = btnrununtilcanceled.IsEnabled = true;
				}
			});

			btnrununtilcanceled=AddButton("run until canceled", async () => {
				if (cts != null)
					cts.Cancel(false);
				cts = new CancellationTokenSource();
				btncancel.IsEnabled = true;
				btnrunonce.IsEnabled = btnrununtilcanceled.IsEnabled = false;
				try {
					await api.GetEvents(ie => {
						log.Dispatcher.BeginInvoke(new Action(
							() => log.Text += string.Format("EVENT: {0}={1}", ie.name, ie.arg)));
					}, cts.Token);
					return true;
				}
				finally {
					btncancel.IsEnabled = false;
					btnrunonce.IsEnabled = btnrununtilcanceled.IsEnabled = true;
				}
			});
		}

		private CancellationTokenSource cts;


		private void APIDebugger(RestRequest request, IRestResponse response, RestClient client)
		{

			StringBuilder sb = new StringBuilder();
			sb.AppendLine(DateTime.Now.ToString());
			sb.AppendFormat("{0} {1} ", request.Method, client.BuildUri(request));
			foreach (var param in request.Parameters)
				sb.AppendFormat("\n ({0}) {1} = '{2}'", param.Type, param.Name, param.Value);

			sb.AppendFormat("\n\nRESPONSE ({0} {1} {2}):\n", response.StatusCode, response.ContentType, response.ContentEncoding);
			sb.Append(response.Content);

			Console.WriteLine(sb.ToString());

			log.Dispatcher.BeginInvoke(new Action(() => {
				log.Text = sb.ToString();
			}));
		}
	}
}
