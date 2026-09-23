package com.example.jamkkaebi.artifact.dto.response;

/**
 * 성장 게이지 현황. 완전각성한 도깨비에게는 내려주지 않는다.
 */
public record GaugeView(int current, int max) {
}
