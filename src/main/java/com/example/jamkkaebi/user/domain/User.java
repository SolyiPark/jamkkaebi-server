package com.example.jamkkaebi.user.domain;

import com.example.jamkkaebi.auth.domain.AuthProvider;
import com.example.jamkkaebi.global.common.BaseTimeEntity;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Index;
import jakarta.persistence.Table;
import jakarta.persistence.UniqueConstraint;
import lombok.AccessLevel;
import lombok.Builder;
import lombok.Getter;
import lombok.NoArgsConstructor;
import org.hibernate.annotations.ColumnDefault;
import org.hibernate.annotations.DynamicUpdate;

import java.time.LocalDate;
import java.time.LocalDateTime;

/**
 * 잠깨비 플레이어 계정.
 *
 * <p><b>식별자가 세 층으로 나뉜다.</b>
 * <ul>
 *   <li>{@code provider + providerUserId} — 소셜이 이 값만 돌려주므로 계정 매칭 기준. 변경 불가.</li>
 *   <li>{@code id} — 내부 PK. 순번이라 노출하면 가입 수·가입 순서가 새므로 <b>내부 전용</b>.</li>
 *   <li>{@code friendCode} — API 응답과 JWT {@code sub} 에 나가는 <b>유일한 외부 식별자</b>.</li>
 * </ul>
 *
 * <p><b>이메일과 프로필 이미지는 수집하지 않는다.</b> 이메일 컬럼은 나중을 위해 자리만 두고 항상
 * {@code null} 이며, 프로필은 소셜 사진이 아니라 사전 제작 아바타({@code spiritId})에서 고른다.
 *
 * <p><b>바뀐 컬럼만 UPDATE 한다</b>({@code @DynamicUpdate}). 정성은 한 문장 UPDATE 로 더하는데, 엔티티가 전체
 * 컬럼을 다시 쓰면 같은 시각에 접속 기록을 갱신한 트랜잭션이 읽어 둔 옛 잔액으로 덮어써 적립분이 사라진다.
 */
@DynamicUpdate
@Entity
@Table(
        name = "users",
        uniqueConstraints = {
                @UniqueConstraint(
                        name = "uk_users_provider_user",
                        columnNames = {"provider", "provider_user_id"}),
                @UniqueConstraint(name = "uk_users_friend_code", columnNames = "friend_code")
        },
        indexes = @Index(name = "idx_users_last_active", columnList = "last_active_at")
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class User extends BaseTimeEntity {

    /** 친구 코드 길이. {@code CHAR(8)} 고정이며 재발급하지 않는다. */
    public static final int FRIEND_CODE_LENGTH = 8;
    /** 닉네임 최대 길이(코드포인트). 소셜 닉네임이 더 길면 잘라서 저장한다. */
    public static final int NICKNAME_MAX_LENGTH = 10;

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 30)
    private AuthProvider provider;

    /** 소셜 서비스가 부여한 고유 회원번호. 외부로 내보내지 않는다. */
    @Column(name = "provider_user_id", nullable = false, length = 100)
    private String providerUserId;

    /** 밖으로 나가는 유일한 식별자. 가입 시 무작위 발급하며 재발급하지 않는다. */
    @Column(name = "friend_code", nullable = false, columnDefinition = "CHAR(8)")
    private String friendCode;

    /** 중복을 허용한다 — 사람을 구분하는 것은 친구 코드이지 닉네임이 아니다. */
    @Column(nullable = false, length = NICKNAME_MAX_LENGTH)
    private String nickname;

    /**
     * 수집하지 않는다. 항상 {@code null} 이며, 나중에 필요해질 때를 위해 컬럼만 둔다.
     */
    @Column(length = 255)
    private String email;

    /** 프로필 아바타(정령) 식별자. {@code null} 이면 기본 사람 모양이다. */
    @Column(name = "spirit_id")
    private Long spiritId;

    /**
     * 마지막으로 이어진 연속 접속 일수. 하루에 한 번이라도 인증된 API 를 부르면 그날을 출석으로 센다.
     * 끊겼는지는 {@link #currentStreak(LocalDate)} 로 판단한다 — 접속하지 않은 날에는 이 값을 고칠 주체가 없다.
     */
    @ColumnDefault("0")
    @Column(name = "streak_days", nullable = false)
    private int streakDays;

    /** 마지막 접속 시각. 친구 목록 정렬과 추천 후보 선정에 쓴다. 접속 기록이 없으면 {@code null}. */
    @Column(name = "last_active_at")
    private LocalDateTime lastActiveAt;

    /** 정성 잔액. 확률형 아이템을 팔지 않으므로 오직 플레이·친구 활동으로만 쌓인다. */
    @ColumnDefault("0")
    @Column(nullable = false)
    private int jeongseong;

    @Builder
    private User(AuthProvider provider, String providerUserId, String friendCode, String nickname) {
        this.provider = provider;
        this.providerUserId = providerUserId;
        this.friendCode = friendCode;
        this.nickname = nickname;
    }

    /** 닉네임을 변경한다. 가입 직후 수정 화면에서 부르는 것을 전제로 한다. */
    public void updateNickname(String nickname) {
        this.nickname = nickname;
    }

    /** 프로필 아바타를 바꾼다. */
    public void updateSpirit(Long spiritId) {
        this.spiritId = spiritId;
    }

    /**
     * 접속을 기록한다.
     *
     * <p>같은 날 두 번째 접속부터는 시각만 갱신한다. 어제 접속했으면 연속 일수를 잇고, 하루 이상
     * 비었으면 1부터 다시 센다.
     */
    public void recordActivity(LocalDateTime now) {
        LocalDate today = now.toLocalDate();
        if (lastActiveAt == null || streakDays == 0) {
            streakDays = 1;
        } else {
            LocalDate lastDay = lastActiveAt.toLocalDate();
            if (lastDay.plusDays(1).equals(today)) {
                streakDays++;
            } else if (today.isAfter(lastDay)) {
                streakDays = 1;
            }
        }
        if (lastActiveAt == null || now.isAfter(lastActiveAt)) {
            lastActiveAt = now;
        }
    }

    /**
     * 오늘 기준으로 이어지고 있는 연속 접속 일수. 어제도 오늘도 접속하지 않았으면 이미 끊긴 것이므로
     * 저장된 값과 무관하게 0 이다 — 친구 목록에 며칠째 안 들어온 친구의 옛 기록이 그대로 보이면 안 된다.
     */
    public int currentStreak(LocalDate today) {
        if (lastActiveAt == null) {
            return 0;
        }
        return lastActiveAt.toLocalDate().plusDays(1).isBefore(today) ? 0 : streakDays;
    }
}
