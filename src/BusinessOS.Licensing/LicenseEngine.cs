using System;
using System.Collections.Generic;
using System.Linq;

namespace BusinessOS.Licensing;

public sealed class LicenseEngine
{
	private readonly LeaseSigner _signer;

	private readonly List<DeviceActivation> _activations = new List<DeviceActivation>();

	public Guid LicenseId { get; }

	public string ProductCode { get; }

	public DateOnly StartsOn { get; private set; }

	public DateOnly ValidUntil { get; private set; }

	public EntitlementSnapshot Entitlements { get; private set; }

	public IReadOnlyList<DeviceActivation> Activations => _activations.AsReadOnly();

	private LicenseEngine(string productCode, DateOnly startsOn, EntitlementSnapshot entitlements, LeaseSigner signer)
	{
		LicenseId = Guid.NewGuid();
		ProductCode = productCode;
		StartsOn = startsOn;
		ValidUntil = startsOn.AddYears(1).AddDays(-1);
		Entitlements = entitlements;
		_signer = signer;
	}

	public static LicenseEngine FromVerifiedPurchase(string productCode, DateOnly purchaseDate, EntitlementSnapshot entitlements, LeaseSigner signer)
	{
		if (string.IsNullOrWhiteSpace(productCode))
		{
			throw new ArgumentException("Product code is required.");
		}
		if (entitlements.DesktopSystems < 1)
		{
			throw new ArgumentOutOfRangeException("entitlements");
		}
		return new LicenseEngine(productCode, purchaseDate, entitlements, signer);
	}

	public SignedLicenseLease Activate(string deviceFingerprint, DateTimeOffset now)
	{
		EnsureActiveSubscription(now);
		if (string.IsNullOrWhiteSpace(deviceFingerprint))
		{
			throw new ArgumentException("Device fingerprint is required.");
		}
		if (_activations.Any(x => x.Active && x.DeviceFingerprint == deviceFingerprint))
		{
			return CreateLease(deviceFingerprint, now);
		}
		if (_activations.Count((DeviceActivation x) => x.Active) >= Entitlements.DesktopSystems)
		{
			throw new InvalidOperationException("No desktop device entitlement is available.");
		}
		_activations.Add(new DeviceActivation(Guid.NewGuid(), deviceFingerprint, Active: true, now));
		return CreateLease(deviceFingerprint, now);
	}

	public SignedLicenseLease ReplaceDevice(string oldFingerprint, string newFingerprint, DateTimeOffset now)
	{
		EnsureActiveSubscription(now);
		int num = _activations.FindLastIndex((DeviceActivation x) => x.Active && x.DeviceFingerprint == oldFingerprint);
		if (num < 0)
		{
			throw new InvalidOperationException("Old device is not active.");
		}
		DeviceActivation deviceActivation = _activations[num];
		_activations[num] = deviceActivation with
		{
			Active = false
		};
		return Activate(newFingerprint, now);
	}

	public void AddDesktopSystems(int quantity)
	{
		if (quantity <= 0)
		{
			throw new ArgumentOutOfRangeException("quantity");
		}
		Entitlements = Entitlements with
		{
			DesktopSystems = Entitlements.DesktopSystems + quantity
		};
	}

	public void Renew(DateOnly paymentDate)
	{
		if (paymentDate <= ValidUntil)
		{
			ValidUntil = ValidUntil.AddYears(1);
			return;
		}
		StartsOn = paymentDate;
		ValidUntil = paymentDate.AddYears(1).AddDays(-1);
	}

	public byte[] ExportPublicKey()
	{
		return _signer.ExportPublicKey();
	}

	private SignedLicenseLease CreateLease(string deviceFingerprint, DateTimeOffset now)
	{
		DateTimeOffset dateTimeOffset = new DateTimeOffset(ValidUntil.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));
		DateTimeOffset dateTimeOffset2 = now.AddDays(7.0);
		if (dateTimeOffset2 > dateTimeOffset)
		{
			dateTimeOffset2 = dateTimeOffset;
		}
		LicenseLeasePayload payload = new LicenseLeasePayload(LicenseId, ProductCode, deviceFingerprint, now, dateTimeOffset2, ValidUntil, Entitlements);
		return _signer.Sign(payload);
	}

	private void EnsureActiveSubscription(DateTimeOffset now)
	{
		if (DateOnly.FromDateTime(now.UtcDateTime) > ValidUntil)
		{
			throw new InvalidOperationException("Subscription has expired.");
		}
	}
}
