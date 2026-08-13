using System;
using UnityEngine;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System.Collections.Generic;
using hive;
#endif

/// Blocks the game from booting until the player is signed in through the Hive SDK.
///
/// After setup we always show Hive's sign-in UI rather than silently reusing an earlier
/// session, so the player picks an account on every launch. The UI is Hive's own
/// (useCustomUI is off in hive_config.xml), so we never draw anything here.
///
/// Batchmode runs unattended with no one around to type credentials, so it boots straight
/// through.
public static class HiveLoginGate
{
	public static bool IsRequired()
	{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
		return !Application.isBatchMode;
#else
		// The Hive native plugin only ships for windows players; in the editor and on other
		// platforms there is nothing to sign in against.
		return false;
#endif
	}

	/// Runs the sign-in flow and invokes onComplete once the player is signed in.
	/// onComplete is not invoked if sign-in fails; Hive shows the error and, for the
	/// failures it considers fatal, asks us to quit.
	public static void SignIn(Action onComplete)
	{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
		s_OnComplete = onComplete;

		HIVEUnityPlugin.InitPlugin();
		Configuration.setGameLanguage("en");

		Debug.Log("Hive: setup");
		AuthV4.setup(OnSetup);
#else
		onComplete();
#endif
	}

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
	static Action s_OnComplete;

	static void OnSetup(ResultAPI result, bool isAutoSignIn, string did, List<AuthV4.ProviderType> providerTypeList)
	{
		Debug.Log("Hive: setup result " + result.toString() + " (autoSignIn:" + isAutoSignIn + ")");

		if (result.needExit())
		{
			Debug.LogError("Hive: setup demands exit");
			Application.Quit(0);
			return;
		}

		if (!result.isSuccess())
		{
			Debug.LogError("Hive: setup failed: " + result.toString());
			return;
		}

		Debug.Log("Hive: showing sign-in UI");
		AuthV4.showSignIn(OnShowSignIn);
	}

	// Explicit sign-in through Hive's own UI.
	static void OnShowSignIn(ResultAPI result, AuthV4.PlayerInfo playerInfo)
	{
		Debug.Log("Hive: showSignIn result " + result.toString());

		if (result.needExit())
		{
			Debug.LogError("Hive: showSignIn demands exit");
			Application.Quit(0);
			return;
		}

		switch (result.code)
		{
			case ResultAPI.Code.Success:
			case ResultAPI.Code.AuthV4PlayerResolved:
			case ResultAPI.Code.AuthV4PlayerChange:
				Complete(playerInfo);
				break;

			default:
				Debug.LogError("Hive: showSignIn failed: " + result.toString());
				break;
		}
	}

	static void Complete(AuthV4.PlayerInfo playerInfo)
	{
		if (playerInfo != null)
			Debug.Log("Hive: signed in as playerId " + playerInfo.playerId);

		var onComplete = s_OnComplete;
		s_OnComplete = null;

		if (onComplete != null)
			onComplete();
	}
#endif
}
