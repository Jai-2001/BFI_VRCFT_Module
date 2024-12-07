namespace BFI_VRCFT_Module
{
    using Microsoft.Extensions.Logging;
    using System.Collections;
    using System.IO;
    using System.Linq.Expressions;
    using System.Net;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using VRCFaceTracking;
    using VRCFaceTracking.Core.Library;
    using VRCFaceTracking.Core.Params.Data;
    using VRCFaceTracking.Core.Params.Expressions;
    using VRCFaceTracking.Core.Types;

    public class BFI_VRCFT_Module : ExtTrackingModule
    {
        //tags with which each expression is associated
        private static string tagEyeClosed = "eyeclosed";

                //osc info
        public static bool debug = false;
        OscReceiver reciever;

        // Expression mapping
        private Dictionary<string, UnifiedExpressionShape> expressionShapes;



        // What your interface is able to send as tracking data.
        public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

        // This is the first function ran by VRCFaceTracking. Make sure to completely initialize 
        // your tracking interface or the data to be accepted by VRCFaceTracking here. This will let 
        // VRCFaceTracking know what data is available to be sent from your tracking interface at initialization.
        public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
        {

            Logger.LogInformation("jZUS_ fork");
            JsonParser parser = new JsonParser();
            Config config = new Config();
            try
            {
                config = parser.ParseConfig();
            }
            catch (Exception ex)
            {
                Logger.LogInformation($"Error parsing JSON file: {ex.Message}");
            }

            Logger.LogInformation(parser.debugString);

            reciever = new OscReceiver(IPAddress.Parse(config.ip),config.port,config.timoutTime) ;

            reciever.StartListening();//starts OSC listener
            Logger.LogInformation(reciever.debugString);
            var state = (eyeAvailable, expressionAvailable);

            ModuleInformation.Name = "BFI Module";

            // Example of an embedded image stream being referenced as a stream
            System.Reflection.Assembly a = System.Reflection.Assembly.GetExecutingAssembly();
            Stream stream = a.GetManifestResourceStream("BFI_VRCFT_Module.Assets.BFI_logo.png");
            // Setting the stream to be referenced by VRCFaceTracking.
            ModuleInformation.StaticImages = stream != null ? new List<Stream> { stream } : ModuleInformation.StaticImages;
            if (debug) Logger.LogInformation("is stream to picture null: " + (stream == null).ToString());
            //... Initializing module. Modify state tuple as needed (or use bool contexts to determine what should be initialized).


            //parsing json file for expressions
            try
            {
                SupportedExpressions expressions = parser.ParseExpressions();  //parsing json file
                reciever.expressions = expressions;                     //assigning expressions to the reciever

                expressionShapes = new Dictionary<string, UnifiedExpressionShape>();

                if (expressions?.Expressions != null)
                {
                        foreach (var expression in expressions.Expressions)
                        {
                        expressionShapes.Add(expression.Key, new UnifiedExpressionShape());
                        Logger.LogInformation($"Expression: {expression.Key}, Id: {expression.Value.Id}, Weight: {expression.Value.ConfigWeight}");//printting supported expressions to console.
                        }
                }
                else
                {
                    Logger.LogInformation($"No expressions found in the JSON file");
                }

            }
            catch (Exception ex)
            {
                Logger.LogInformation($"Error parsing JSON file: {ex.Message}");
                return (false, false);
            }
            return state;


        }

        // Polls data from the tracking interface.
        // VRCFaceTracking will run this function in a separate thread;
        public override void Update()
        {
            reciever.EvaluateTimout();
            // Get latest tracking data from interface and transform to VRCFaceTracking data.

            if (Status == ModuleState.Active) // Module Status validation
            {
                // ... Execute update cycle.


                if (debug) Logger.LogInformation(reciever.debugString);

                try
                {
                    //UpdateValues();
                    UpdateValuesExpressions();
                }
                catch (Exception ex)
                {
                    Logger.LogInformation($"Update error: {ex.Message}");
                }

                if (reciever.EvaluateTimout())//checkerboard eyes if we didn't recieve any data for a while
                {
                    UnifiedTracking.Data.Eye.Left.Gaze = new Vector2(-.75f, 0);
                    UnifiedTracking.Data.Eye.Right.Gaze = new Vector2(.75f, 0);

                    UnifiedTracking.Data.Eye.Left.Openness = 1f;
                    UnifiedTracking.Data.Eye.Right.Openness = 1f;
                }
                else
                {
                    UnifiedTracking.Data.Eye.Left.Gaze = new Vector2(0, 0);
                    UnifiedTracking.Data.Eye.Right.Gaze = new Vector2(0, 0);

                    if (reciever.expressions.Expressions.ContainsKey(tagEyeClosed))//assinign eyeclosed weights if the expression is present
                    {

                        UnifiedTracking.Data.Eye.Left.Openness = 1 - reciever.expressions.Expressions[tagEyeClosed].Weight;
                        UnifiedTracking.Data.Eye.Right.Openness = 1 - reciever.expressions.Expressions[tagEyeClosed].Weight;

                    }
                    else
                    {

                        UnifiedTracking.Data.Eye.Left.Openness = 1f;
                        UnifiedTracking.Data.Eye.Right.Openness = 1f;
                    }
                }

                Logger.LogInformation($"Shapes Loop");

            }

            // Add a delay or halt for the next update cycle for performance. eg: 
            Thread.Sleep(10);
        }

        private void UpdateValuesExpressions()//sets values of expressions based on the values recieved from OSC if present
        {
            try
            {
                Logger.LogInformation($"Update Loop");
                foreach (var expression in reciever.expressions.Expressions)
                {
                    if (expressionShapes.ContainsKey(expression.Key))
                    {
                        UnifiedExpressionShape s = expressionShapes[expression.Key];
                        s.Weight = Clampf01(expression.Value.Weight);
                    }
                }

                // Handle interactions if necessary (based on the expressions' config)
                HandleExpressionInteractions();
            
            }
            catch (Exception ex)
            {
                Logger.LogInformation($"Error trying to acces values: {ex.Message}");
            }
        }

        // Handle interactions between expressions dynamically
        private void HandleExpressionInteractions()
        {
            Logger.LogInformation($"Handle Loop");
            // Example: Implement interaction logic based on your config.json structure
            foreach (var expression in reciever.expressions.Expressions)
            {
                var interactions = expression.Value.Interactions; // Assuming interactions are part of the expression config
                if (interactions != null)
                {
                    foreach (var interaction in interactions)
                    {
                        string interactingExpression = interaction.Key;
                        float interactionValue = interaction.Value;
                        Logger.LogInformation($"Handle Loop ({expression.Key}) - {interactingExpression}:{interactionValue}");
                        if (expressionShapes.ContainsKey(interactingExpression))
                        {
                            UnifiedExpressionShape s = expressionShapes[interactingExpression];
                            s.Weight = Clampf01(s.Weight + interactionValue);
                        }
                    }
                }
            }
        }

        // Called when the module is unloaded or VRCFaceTracking itself tears down.
        public override void Teardown()
        {
            //... Deinitialize tracking interface; dispose any data created with the module.

            //resets the face to neutral uppon closing the app
            UnifiedTracking.Data.Eye.Left.Gaze = new Vector2(0, 0);
            UnifiedTracking.Data.Eye.Right.Gaze = new Vector2(0, 0);

            foreach (var shape in expressionShapes)
            {
                UnifiedExpressionShape s = shape.Value;
                s.Weight = 0;
            }

            UnifiedTracking.Data.Eye.Left.Openness = 1;
            UnifiedTracking.Data.Eye.Right.Openness = 1;

            foreach (var shape in expressionShapes)
            {
                UnifiedTracking.Data.Shapes[(int)Enum.Parse(typeof(UnifiedExpressions), shape.Key)] = shape.Value;
            }

        }

        float map(float x, float in_min, float in_max, float out_min, float out_max)//remapping function, could prove useful
        {
            return (x - in_min) * (out_max - out_min) / (in_max - in_min) + out_min;
        }

        private float Clampf01(float value)//clamps value between 0 and 1
        {
            return Math.Clamp(value, 0, 1);
        }

        private float ClampfMinus11(float value)//clamps value between 0 and 1
        {
            return Math.Clamp(value, -1, 1);
        }

    }

}