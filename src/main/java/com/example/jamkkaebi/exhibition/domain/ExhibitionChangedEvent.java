package com.example.jamkkaebi.exhibition.domain;

import java.time.LocalDateTime;

/**
 * 사용자가 자기 전시관을 수정했다는 기록.
 *
 * <p><b>전시관을 고친 그 트랜잭션 안에서, 사용자 행을 첫 읽기로 잠근 뒤 발행한다.</b> 스냅샷이 같은
 * 트랜잭션에서 갱신되어 저장과 함께 반영되거나 함께 되돌아간다.
 *
 * @param userId    전시관 주인
 * @param changedAt 고친 시각
 */
public record ExhibitionChangedEvent(Long userId, LocalDateTime changedAt) {
}
