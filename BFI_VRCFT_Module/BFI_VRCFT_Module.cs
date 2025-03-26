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
                    
                    if (reciever.expressions.Expressions.TryGetValue("eyeclosed", out var eyeClosedExpr))//assigning eyeclosed weights if the expression is present
                    {
                        float openness = 1 - eyeClosedExpr.Weight;
                        UnifiedTracking.Data.Eye.Left.Openness = openness;
                        UnifiedTracking.Data.Eye.Right.Openness = openness;
                    }
                    else
                    {

                        UnifiedTracking.Data.Eye.Left.Openness = 1f;
                        UnifiedTracking.Data.Eye.Right.Openness = 1f;
                    }
                }

            }

            // Add a delay or halt for the next update cycle for performance. eg: 
            Thread.Sleep(10);
        }

        private void UpdateValuesExpressions()//sets values of expressions based on the values recieved from OSC if present
        {
            try
            {
                var expressions = reciever.expressions.Expressions;

                // Process expressions in ID order to maintain interaction hierarchy
                var orderedExpressions = expressions
                    .OrderBy(e => e.Value.Id)
                    .ToList();

                // First pass: Apply base weights
                foreach (var entry in orderedExpressions)
                {
                    var expressionKey = entry.Key;
                    var expression = entry.Value;

                    if (expressionShapes.TryGetValue(expressionKey, out var shape))
                    {
                        // Apply base weight with proper range clamping
                        shape.Weight = ClampToRange(expression.Weight, expression);

                        // Apply shape to UnifiedExpression targets
                        foreach (var targetName in expression.Targets)
                        {
                            if (Enum.TryParse<UnifiedExpressions>(targetName, out var target))
                            {
                                UnifiedTracking.Data.Shapes[(int)target] = shape;
                            }
                        }
                    }
                }

                // Second pass: Apply interactions
                foreach (var entry in orderedExpressions)
                {
                    var expressionKey = entry.Key;
                    var expression = entry.Value;

                    if (expression.Interactions != null && expressionShapes.TryGetValue(expressionKey, out var shape))
                    {
                        foreach (var interaction in expression.Interactions)
                        {
                            var targetKey = interaction.Key;
                            if (!expressions.TryGetValue(targetKey, out var targetExpression))
                                continue;

                            // Only apply to higher IDs to maintain order
                            if (targetExpression.Id <= expression.Id)
                                continue;

                            if (expressionShapes.TryGetValue(targetKey, out var targetShape))
                            {
                                float interactionValue = interaction.Value;
                                targetShape.Weight = ClampToRange(
                                    targetShape.Weight + (shape.Weight * interactionValue),
                                    targetExpression
                                );
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogInformation($"Error updating expressions: {ex.Message}");
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

            if (reciever.expressions?.Expressions != null)
            {
                foreach (var entry in reciever.expressions.Expressions)
                {
                    foreach (var targetName in entry.Value.Targets)
                    {
                        if (Enum.TryParse<UnifiedExpressions>(targetName, out var target))
                        {
                            UnifiedTracking.Data.Shapes[(int)target] = new UnifiedExpressionShape();
                        }
                    }
                }
            }

        }

        float map(float x, float in_min, float in_max, float out_min, float out_max)//remapping function, could prove useful
        {
            return (x - in_min) * (out_max - out_min) / (in_max - in_min) + out_min;
        }

        private float Clamp(float value, float min, float max)
        {
            return Math.Min(Math.Max(value, min), max);
        }

        private float ClampToRange(float value, Expression expression)
        {
            if (expression.Range == null || expression.Range.Length != 2)
                return Clamp(value, 0, 1);

            return Clamp(value, expression.Range[0], expression.Range[1]);
        }

    }

}