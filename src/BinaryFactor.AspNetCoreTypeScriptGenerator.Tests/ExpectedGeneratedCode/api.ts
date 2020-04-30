﻿// This file is autogenerated, any manual changes will be lost after regeneration
import * as dto from './dto';
import * as enums from './enums';
import axios from 'axios';


export namespace SampleController {
    export async function getNestedDtos(enumParameter: enums.EnumType): Promise<dto.NestedDto[]> {
        const response = await axios.request({
            url: '/Sample/GetNestedDtos',
            method: 'GET',
            params: { enumParameter },
            data: null
        });

        return response.data
    }

    export async function getNestedDtosWithNullableParam(nullableEnumParameter?: enums.EnumType | null): Promise<dto.NestedDto[]> {
        const response = await axios.request({
            url: '/Sample/GetNestedDtosWithNullableParam',
            method: 'GET',
            params: { nullableEnumParameter },
            data: null
        });

        return response.data
    }

    export async function getIntegers(): Promise<(number | undefined | null)[]> {
        const response = await axios.request({
            url: '/Sample/GetIntegers',
            method: 'GET',
            params: {  },
            data: null
        });

        return response.data
    }

    export async function getGenericDto(): Promise<dto.GenericDto<string | undefined | null, dto.NestedDto>> {
        const response = await axios.request({
            url: '/Sample/GetGenericDto',
            method: 'GET',
            params: {  },
            data: null
        });

        return response.data
    }

    export async function getGenericDtoWithList(): Promise<dto.GenericDto<number, (string | undefined | null)[]>> {
        const response = await axios.request({
            url: '/Sample/GetGenericDtoWithList',
            method: 'GET',
            params: {  },
            data: null
        });

        return response.data
    }

    export async function getWrappedDateTime(): Promise<dto.NullableValueTypeWrapper<Date>> {
        const response = await axios.request({
            url: '/Sample/GetWrappedDateTime',
            method: 'GET',
            params: {  },
            data: null
        });

        return response.data
    }

    export async function getWrappedString(): Promise<dto.NullableReferenceTypeWrapper<string | undefined | null>> {
        const response = await axios.request({
            url: '/Sample/GetWrappedString',
            method: 'GET',
            params: {  },
            data: null
        });

        return response.data
    }

    export async function getMaybeNullReturn(str?: string | null): Promise<dto.NonGenericDto | undefined | null> {
        const response = await axios.request({
            url: '/Sample/GetMaybeNullReturn',
            method: 'GET',
            params: { str },
            data: null
        });

        return response.data
    }

    export async function receiveDtoWithAllowNull(nonGenericDto?: dto.NonGenericDto | null): Promise<void> {
        const response = await axios.request({
            url: '/Sample/ReceiveDtoWithAllowNull',
            method: 'GET',
            params: {  },
            data: nonGenericDto
        });

        return response.data
    }

    export async function getNumberDictionary(): Promise<{[key: number]: string | undefined | null}> {
        const response = await axios.request({
            url: '/Sample/GetNumberDictionary',
            method: 'GET',
            params: {  },
            data: null
        });

        return response.data
    }

    export async function getStringDictionary(): Promise<{[key: string]: string | undefined | null}> {
        const response = await axios.request({
            url: '/Sample/GetStringDictionary',
            method: 'GET',
            params: {  },
            data: null
        });

        return response.data
    }

    export async function getEnumDictionary(): Promise<{[K in enums.EnumType]: boolean}> {
        const response = await axios.request({
            url: '/Sample/GetEnumDictionary',
            method: 'GET',
            params: {  },
            data: null
        });

        return response.data
    }

    export async function getMaybeNullObjectReturn(number?: number | null): Promise<any | undefined | null> {
        const response = await axios.request({
            url: '/Sample/GetMaybeNullObjectReturn',
            method: 'GET',
            params: { number },
            data: null
        });

        return response.data
    }
}
