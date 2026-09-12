using System;
using System.Collections;
using UnityEngine;

public sealed class StreamingValidationRunner : MonoBehaviour
{
    private IEnumerator Start()
    {
        yield return null;
        int exitCode = 0;
        try { StreamingValidation.Run(); }
        catch (Exception exception) { Debug.LogException(exception); exitCode = 1; }
        Application.Quit(exitCode);
    }
}
