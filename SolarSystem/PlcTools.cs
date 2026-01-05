namespace PlcTools
{
    /// <summary>
    /// Rising edge trigger - detects transitions from false to true
    /// </summary>
    public class RTrigger
    {
        private bool _previousState = false;

        /// <summary>
        /// Clock the trigger with current state
        /// Returns true only on the rising edge (false -> true transition)
        /// </summary>
        public bool CLK(bool currentState)
        {
            bool risingEdge = currentState && !_previousState;
            _previousState = currentState;
            return risingEdge;
        }
    }
}
