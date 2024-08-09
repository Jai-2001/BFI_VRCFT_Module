﻿using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using VRCFaceTracking.Core.OSC;
using VRCFaceTracking.OSC;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace BFI_VRCFT_Module
{
    
public class OscReceiver
{
    private UdpClient _udpClient;
    private IPEndPoint _endPoint;
    private int Port;
    private static string _oscAddress = "/BFI/MLAction/Action";

    private Stopwatch timer = new Stopwatch();
    public static double timeoutTime = 3;//timout time in seconds

    public string OSCDebugData;//debug string to display in the console

    public float eyeClosed = 0;
    public float smile = 0;
    public float frown = 0;
    public float anger = 0;
    public float cringe = 0;


    public OscReceiver(int port)
    {
        Port = port;
        _endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), port);
        _udpClient = new UdpClient(_endPoint);
    }

    public async Task StartListening()
    {
        Console.WriteLine($"Listening for OSC messageson port {Port} ...");
        timer.Start();
        while (true)
        {
            var result = await _udpClient.ReceiveAsync();
            HandleOscMessage(result.Buffer);
        }
    }
    public bool EvaluateTimout()
    {
        return timer.Elapsed.TotalSeconds > timeoutTime;
    }

    private void HandleOscMessage(byte[] bytes)
    {
        int messageIndex = 0;
        OscMessage oscMessage = new OscMessage(bytes, bytes.Length, ref messageIndex);
        
        if (oscMessage != null)
        {
                if (oscMessage.Address.ToString().Contains(_oscAddress + "1"))
                {
                    eyeClosed = (float)oscMessage.Value;
                    resetStopwatch();
                }
                else if (oscMessage.Address.ToString().Contains(_oscAddress + "2"))
                {
                    smile = (float)oscMessage.Value;
                    resetStopwatch();
                }
                else if (oscMessage.Address.ToString().Contains(_oscAddress + "3"))
                {
                    frown = (float)oscMessage.Value;
                    resetStopwatch();
                }
                else if (oscMessage.Address.ToString().Contains(_oscAddress + "4"))
                {
                    anger = (float)oscMessage.Value;
                    resetStopwatch();
                }
                else if (oscMessage.Address.ToString().Contains(_oscAddress + "5"))
                {
                    cringe = (float)oscMessage.Value;
                    resetStopwatch();
                }


                OSCDebugData = $"RawData{oscMessage.Value}\nEyeClosed = {eyeClosed.ToString()} \nSmile = {smile.ToString()}\nFrown = {frown.ToString()}\nAnger = {anger.ToString()}\ncringe = {cringe.ToString()}\n\n";

            }
        else
        {
               OSCDebugData = "Failed to parse OSC message.";
        }
    }

    private void resetStopwatch(){
        timer.Reset();
        timer.Restart();
    }
}
}