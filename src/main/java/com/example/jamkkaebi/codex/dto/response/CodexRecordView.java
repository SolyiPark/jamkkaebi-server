package com.example.jamkkaebi.codex.dto.response;

import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * 완료한 유물에만 열리는 실제 발굴·복원 기록과
 * 실물 문화재로 나가는 링크(미정)
 *
 * <p>링크는 확정되기 전이라 값이 없을 수 있다(없으면 키 생략)
 *
 * @param story       실제 발굴·복원 기록
 * @param heritageUrl 국가유산 정보 페이지
 * @param reportUrl   훼손 신고 채널
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record CodexRecordView(String story, String heritageUrl, String reportUrl) {
}
