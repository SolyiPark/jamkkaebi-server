package com.example.jamkkaebi.workbench.dto.request;

/**
 * 도깨비 이름 짓기.
 *
 * @param name 새 이름. {@code null} 이면 기본 이름으로 되돌린다. 길이·금칙어 검사는 서비스에서
 *             하며 통과 못 한 사유는 {@code errors[].field = "name"} 로 돌려준다
 */
public record SpiritNameUpdateRequest(String name) {
}
