package com.example.jamkkaebi.exhibition.domain;

import com.example.jamkkaebi.global.common.BaseTimeEntity;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import jakarta.persistence.UniqueConstraint;
import lombok.AccessLevel;
import lombok.Getter;
import lombok.NoArgsConstructor;

import java.time.LocalDate;

/**
 * 내 전시관 방문 기록. 내가 내 전시관을 연 날마다 한 행이다 (하루에 한 행).
 *
 * <p>도깨비가 나를 '발굴자'가 아닌 닉네임으로 부를 만큼 친해졌는지 가리는 데 쓴다 — 친밀도는 최근 30일 동안 방문한 날 수로 정한다.
 * 친구 전시관 방문 기록({@code visit_logs})과는 따로 둔다. 그쪽은 "누가 누구의 전시관에 왔는지"라 내 전시관을 연 것을 넣으면
 * 방문 보상·방문 표시가 틀어진다. 최초 정화는 전시관을 여는 일이 아니라서 여기에 들어가지 않는다.
 */
@Entity
@Table(
        name = "exhibition_visits",
        uniqueConstraints = @UniqueConstraint(
                name = "uk_exhibition_visits_day", columnNames = {"user_id", "visit_date"})
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class ExhibitionVisit extends BaseTimeEntity {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "user_id", nullable = false)
    private Long userId;

    @Column(name = "visit_date", nullable = false)
    private LocalDate visitDate;

    private ExhibitionVisit(Long userId, LocalDate visitDate) {
        this.userId = userId;
        this.visitDate = visitDate;
    }

    public static ExhibitionVisit of(Long userId, LocalDate visitDate) {
        return new ExhibitionVisit(userId, visitDate);
    }
}
