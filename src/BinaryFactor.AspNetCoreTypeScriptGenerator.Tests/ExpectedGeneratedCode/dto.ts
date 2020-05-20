﻿// This file is autogenerated, any manual changes will be lost after regeneration


export interface GenericDto<K, V> {
    key: K;
    value: V;
}

export interface NestedDto {
    valueNullableByMaybeNull?: NonGenericDto | null;
    firstNameNullableByStringRule?: string | null;
    lastNameNotNullByNotNullAttribute: string;
    lastNameNotNullByDisallowNullAttribute: string;
}

export interface NonGenericDto {
    value?: string | null;
}

export interface NullableReferenceTypeWrapper<T> {
    maybeNullValue?: T | null;
    notNullValue: T;
}

export interface NullableValueTypeWrapper<T> {
    nullableValue?: T | null;
    notNullValue: T;
}
