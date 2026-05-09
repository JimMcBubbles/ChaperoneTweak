//======= Copyright (c) Valve Corporation, All rights reserved. ===============
//
// Purpose: For controlling in-game objects with tracked devices.
//
//=============================================================================

using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;
using Valve.VR;

public class SteamVR_TrackedObject : MonoBehaviour
{
	public enum EIndex
	{
		None = -1,
		Hmd = (int)OpenVR.k_unTrackedDeviceIndex_Hmd,
		Device1,
		Device2,
		Device3,
		Device4,
		Device5,
		Device6,
		Device7,
		Device8,
		Device9,
		Device10,
		Device11,
		Device12,
		Device13,
		Device14,
		Device15
	}

	public EIndex index;
	public Transform origin; // if not set, relative to parent
    public bool isValid = false;

	private void OnNewPoses(params object[] args)
	{
		if (index == EIndex.None)
			return;

		var i = (int)index;

        isValid = false;
		var poses = (Valve.VR.TrackedDevicePose_t[])args[0];
		if (poses.Length <= i)
			return;

		if (!poses[i].bDeviceIsConnected)
			return;

		if (!poses[i].bPoseIsValid)
			return;

        isValid = true;

		var pose = new SteamVR_Utils.RigidTransform(poses[i].mDeviceToAbsoluteTracking);

		if (origin != null)
		{
			pose = new SteamVR_Utils.RigidTransform(origin) * pose;
			pose.pos.x *= origin.localScale.x;
			pose.pos.y *= origin.localScale.y;
			pose.pos.z *= origin.localScale.z;
			transform.position = pose.pos;
			transform.rotation = pose.rot;
		}
		else
		{
			transform.localPosition = pose.pos;
			transform.localRotation = pose.rot;
		}
	}

	void OnEnable()
	{
		if (!SteamVR.active)
			return; // no SteamVR — Update() will poll XR devices instead

		SteamVR_Utils.Event.Listen("new_poses", OnNewPoses);
	}

	void OnDisable()
	{
		SteamVR_Utils.Event.Remove("new_poses", OnNewPoses);
		isValid = false;
	}

	float _logTimer;

	void Update()
	{
		if (SteamVR.active) return; // SteamVR handles it via new_poses
		if (index == EIndex.None) return;

		bool log = (_logTimer -= Time.deltaTime) <= 0;
		if (log) _logTimer = 3f;

		InputDeviceCharacteristics chars;
		XRNode node;
		if (index == EIndex.Hmd)
		{
			chars = InputDeviceCharacteristics.HeadMounted;
			node  = XRNode.Head;
		}
		else if (index == EIndex.Device1)
		{
			chars = InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller;
			node  = XRNode.RightHand;
		}
		else if (index == EIndex.Device2)
		{
			chars = InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller;
			node  = XRNode.LeftHand;
		}
		else return;

		var devices = new List<InputDevice>();
		InputDevices.GetDevicesWithCharacteristics(chars, devices);
		if (devices.Count > 0)
		{
			var dev = devices[0];
			if (dev.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos))
				transform.localPosition = pos;
			if (dev.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
				transform.localRotation = rot;
			if (log) Debug.Log("[XRTrack] " + index + " pos=" + transform.localPosition);
			return;
		}

		// Fallback: XRNodeState
		var nodeStates = new List<XRNodeState>();
		InputTracking.GetNodeStates(nodeStates);
		foreach (var state in nodeStates)
		{
			if (state.nodeType != node) continue;
			if (state.TryGetPosition(out Vector3 npos)) transform.localPosition = npos;
			if (state.TryGetRotation(out Quaternion nrot)) transform.localRotation = nrot;
			if (log) Debug.Log("[XRTrack] NodeState " + index + " pos=" + transform.localPosition);
			return;
		}

		if (log) Debug.Log("[XRTrack] NO device for " + index);
	}

	public void SetDeviceIndex(int index)
	{
		if (System.Enum.IsDefined(typeof(EIndex), index))
			this.index = (EIndex)index;
	}
}
