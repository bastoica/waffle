#if NoDelayPolicy

namespace TorchLiteRuntime
{
    class DelayInjectionPolicy
    {
        public DelayInjectionPolicy(Log4NetLogger logEntity, HighResolutionDateTime clock) 
        { 
            /* Do nothing */
        }

        /// <summary>
        /// </summary>
        /// <param name="fieldName"></param>
        /// <param name="caller"></param>
        /// <param name="ilOffset"></param>
        public void BeforeFieldWrite(TorchPoint tp) 
        { 
            /* Do nothing */
        }

        /// <summary>
        /// </summary>
        /// <param name="fieldName"></param>
        /// <param name="caller"></param>
        /// <param name="ilOffset"></param>
        public void BeforeMethodCall(TorchPoint tp) 
        { 
            /* Do nothing */
        }
    }
}

#endif