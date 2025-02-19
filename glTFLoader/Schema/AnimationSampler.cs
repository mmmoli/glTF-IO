//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace glTFLoader.Schema {
    using System.Linq;
    using System.Runtime.Serialization;
    
    
    public class AnimationSampler {
        
        /// <summary>
        /// Backing field for Input.
        /// </summary>
        private int m_input;
        
        /// <summary>
        /// Backing field for Interpolation.
        /// </summary>
        private InterpolationEnum m_interpolation = InterpolationEnum.LINEAR;
        
        /// <summary>
        /// Backing field for Output.
        /// </summary>
        private int m_output;
        
        /// <summary>
        /// Backing field for Extensions.
        /// </summary>
        private System.Collections.Generic.Dictionary<string, object> m_extensions;
        
        /// <summary>
        /// Backing field for Extras.
        /// </summary>
        private Extras m_extras;
        
        /// <summary>
        /// The index of an accessor containing keyframe input values, e.g., time.
        /// </summary>
        [Newtonsoft.Json.JsonRequiredAttribute()]
        [Newtonsoft.Json.JsonPropertyAttribute("input")]
        public int Input {
            get {
                return this.m_input;
            }
            set {
                if ((value < 0)) {
                    throw new System.ArgumentOutOfRangeException("Input", value, "Expected value to be greater than or equal to 0");
                }
                this.m_input = value;
            }
        }
        
        /// <summary>
        /// Interpolation algorithm.
        /// </summary>
        [Newtonsoft.Json.JsonConverterAttribute(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        [Newtonsoft.Json.JsonPropertyAttribute("interpolation")]
        public InterpolationEnum Interpolation {
            get {
                return this.m_interpolation;
            }
            set {
                this.m_interpolation = value;
            }
        }
        
        /// <summary>
        /// The index of an accessor, containing keyframe output values.
        /// </summary>
        [Newtonsoft.Json.JsonRequiredAttribute()]
        [Newtonsoft.Json.JsonPropertyAttribute("output")]
        public int Output {
            get {
                return this.m_output;
            }
            set {
                if ((value < 0)) {
                    throw new System.ArgumentOutOfRangeException("Output", value, "Expected value to be greater than or equal to 0");
                }
                this.m_output = value;
            }
        }
        
        /// <summary>
        /// Dictionary object with extension-specific objects.
        /// </summary>
        [Newtonsoft.Json.JsonPropertyAttribute("extensions")]
        public System.Collections.Generic.Dictionary<string, object> Extensions {
            get {
                return this.m_extensions;
            }
            set {
                this.m_extensions = value;
            }
        }
        
        /// <summary>
        /// Application-specific data.
        /// </summary>
        [Newtonsoft.Json.JsonPropertyAttribute("extras")]
        public Extras Extras {
            get {
                return this.m_extras;
            }
            set {
                this.m_extras = value;
            }
        }
        
        public bool ShouldSerializeInterpolation() {
            return ((m_interpolation == InterpolationEnum.LINEAR) 
                        == false);
        }
        
        public bool ShouldSerializeExtensions() {
            return ((m_extensions == null) 
                        == false);
        }
        
        public bool ShouldSerializeExtras() {
            return ((m_extras == null) 
                        == false);
        }
        
        public enum InterpolationEnum {
            
            LINEAR,
            
            STEP,
            
            CUBICSPLINE,
        }
    }
}
